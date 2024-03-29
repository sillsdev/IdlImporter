// Copyright (c) 2002-2015 SIL International
// This software is licensed under the LGPL, version 2.1 or later
// (http://www.gnu.org/licenses/lgpl-2.1.html)
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using Newtonsoft.Json;

namespace SIL.IdlImporterTool
{
	/// ----------------------------------------------------------------------------------------
	/// <summary>
	/// Imports the interfaces of an IDL file.
	/// </summary>
	/// ----------------------------------------------------------------------------------------
	public class IDLImporter
	{
		private static IdhCommentProcessor s_idhProcessor;
		internal static Dictionary<string, IdhCommentProcessor.CommentInfo> s_MoreComments =
			new Dictionary<string,IdhCommentProcessor.CommentInfo>();
		private EventWaitHandle _waitHandle;

		public IDLImporter()
		{
			Logger = new ConsoleLogger();
		}

		public IDLImporter(ILog logger)
		{
			Logger = logger;
		}

		public static ILog Logger { get; private set; }

		private static CodeCommentStatementCollection AddFileBanner(string sInFile, string sOutFile)
		{
			var coll = new CodeCommentStatementCollection {
				new CodeCommentStatement("--------------------------------------------------------------------------------------------"),
				new CodeCommentStatement($"Copyright (c) {DateTime.Now.Year}, SIL International. All rights reserved."),
				new CodeCommentStatement(""),
				new CodeCommentStatement("File: " + Path.GetFileName(sOutFile)),
				new CodeCommentStatement("Responsibility: Generated by IDLImporter"),
				new CodeCommentStatement("Last reviewed: "),
				new CodeCommentStatement(""),
				new CodeCommentStatement("<remarks>"),
				new CodeCommentStatement("Generated by IDLImporter from file " +
										Path.GetFileName(sInFile)),
				new CodeCommentStatement(""),
				new CodeCommentStatement(
					"You should use these interfaces when you access the COM objects defined in the mentioned"),
				new CodeCommentStatement("IDL/IDH file."),
				new CodeCommentStatement("</remarks>"),
				new CodeCommentStatement("--------------------------------------------------------------------------------------------")
			};

			return coll;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Add comments to each class and method
		/// </summary>
		/// <param name="types">Collection of types</param>
		/// ------------------------------------------------------------------------------------
		private static void AddComments(CodeTypeDeclarationCollection types)
		{
			foreach (CodeTypeDeclaration type in types)
			{
				// we probably inherited the comments (from a base class in an external file),
				// so we don't want to add the same comments again!
				if (type.Comments.Count > 0)
					continue;

				var comment = type.Name;
				if (!s_idhProcessor.Comments.TryGetValue(type.Name, out var ifaceComment) && type.Name != string.Empty)
					s_idhProcessor.Comments.TryGetValue(type.Name.Substring(1), out ifaceComment);

				// Also get comments for base class - if we derive from a class
				// we might need to get some comments from there if we don't have our own.
				var baseComments =
					new List<IdhCommentProcessor.CommentInfo>();

				if (type.BaseTypes.Count > 0)
				{
					for (var i = 0; i < type.BaseTypes.Count; i++)
					{
						if (!s_idhProcessor.Comments.TryGetValue(type.BaseTypes[i].BaseType,
							out var baseComment))
						{
							s_idhProcessor.Comments.TryGetValue(type.BaseTypes[i].BaseType.Substring(1),
								out baseComment);
						}
						if (baseComment != null)
							baseComments.Add(baseComment);
					}
				}

				if (ifaceComment != null)
					comment = ifaceComment.Comment;

				type.Comments.Add(new CodeCommentStatement(
					string.Format(comment.Length > 80 ? "<summary>{1}{0}{1}</summary>" :
					"<summary>{0} </summary>", comment, Environment.NewLine), true));

				foreach (CodeTypeMember member in type.Members)
				{
					if ((!type.IsInterface && !type.IsEnum &&
						(member.Attributes & MemberAttributes.Private) == MemberAttributes.Private)
						|| member.Comments.Count > 0 || member.Name == string.Empty)
					{
						continue;
					}

					IdhCommentProcessor.CommentInfo methodComment = null;
					ifaceComment?.Children.TryGetValue(member.Name, out methodComment);

					for (var i = 0; i < baseComments.Count && methodComment == null; i++)
						baseComments[i].Children.TryGetValue(member.Name, out methodComment);

					switch (member)
					{
						case CodeMemberMethod memberMethod:
						{
							if (methodComment == null)
							{
								// Maybe it's a property with a parameter? Try and see if the IDH
								// file has a comment for a method without the "get_" or "set_"
								if (memberMethod.Name.StartsWith("get_") || memberMethod.Name.StartsWith("set_"))
								{
									var name = memberMethod.Name.Substring(4);
									ifaceComment?.Children.TryGetValue(name, out methodComment);

									for (var i = 0; i < baseComments.Count && methodComment == null; i++)
										baseComments[i].Children.TryGetValue(name, out methodComment);
								}
							}

							comment = "Member " + memberMethod.Name;
							if (methodComment != null)
								comment = methodComment.Comment;

							memberMethod.Comments.Add(new CodeCommentStatement(
								string.Format(comment.Length > 80 ? "<summary>{1}{0}{1}</summary>" :
									"<summary>{0} </summary>", comment, Environment.NewLine), true));

							var method = memberMethod;
							foreach (CodeParameterDeclarationExpression param in method.Parameters)
							{
								IdhCommentProcessor.CommentInfo paramComment = null;
								methodComment?.Children.TryGetValue(param.Name, out paramComment);

								comment = string.Empty;
								if (paramComment != null)
									comment = paramComment.Comment;
								memberMethod.Comments.Add(new CodeCommentStatement(
									$"<param name='{param.Name}'>{comment} </param>",
									true));
							}

							if (method.ReturnType.BaseType != "System.Void")
							{
								comment = "A " + method.ReturnType.BaseType;
								if (methodComment != null && methodComment.Attributes.ContainsKey("retval"))
								{
									var retparamName = methodComment.Attributes["retval"];
									if (methodComment.Children.ContainsKey(retparamName))
										comment = methodComment.Children[retparamName].Comment;
								}
								memberMethod.Comments.Add(new CodeCommentStatement(
									$"<returns>{comment}</returns>", true));
							}

							break;
						}
						case CodeMemberProperty memberProperty:
						{
							var property = memberProperty;

							var getset = string.Empty;
							if (methodComment == null)
							{
								// No comment from IDH file - generate a pseudo one
								if (property.HasGet)
									getset += "Gets";
								if (property.HasSet)
								{
									if (getset.Length > 0)
										getset += "/";
									getset += "Sets";
								}
								getset = $"{getset} a {memberProperty.Name}";
							}
							else
							{
								// Use comment provided in IDH file
								getset = methodComment.Comment;
							}

							memberProperty.Comments.Add(new CodeCommentStatement(
								string.Format(getset.Length > 80 ? "<summary>{1}{0}{1}</summary>" :
									"<summary>{0} </summary>", getset, Environment.NewLine), true));
							memberProperty.Comments.Add(new CodeCommentStatement(
								$"<returns>A {property.Type.BaseType} </returns>", true));
							break;
						}
						case CodeMemberField _:
						{
							comment = methodComment == null ? string.Empty : methodComment.Comment;

							member.Comments.Add(new CodeCommentStatement(
								string.Format(comment.Length > 80 ? "<summary>{1}{0}{1}</summary>" :
									"<summary>{0} </summary>", comment, Environment.NewLine), true));
							break;
						}
						default:
						{
							comment = "Member " + member.Name;
							if (methodComment != null)
								comment = methodComment.Comment;

							member.Comments.Add(new CodeCommentStatement(
								string.Format(comment.Length > 80 ? "<summary>{1}{0}{1}</summary>" :
									"<summary>{0} </summary>", comment, Environment.NewLine), true));

							member.Comments.Add(new CodeCommentStatement(
								$"Not expecting a member of type {member.GetType()}"));
							break;
						}
					}

					if (methodComment == null || !methodComment.Attributes.ContainsKey("exception"))
						continue;

					var exceptions = methodComment.Attributes["exception"].Split(',');
					foreach (var exception in exceptions)
					{
						comment = methodComment.Attributes[exception];
						member.Comments.Add(new CodeCommentStatement(
							$@"<exception cref=""{exception}"">{comment}</exception>", true));
					}
				}
			}
		}



		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Do the actual import
		/// </summary>
		/// <param name="usingNamespaces">Additional imported namespaces</param>
		/// <param name="sFileName">Filename of the IDL file</param>
		/// <param name="sXmlFile">Name of the XML config file</param>
		/// <param name="sOutFile">Output</param>
		/// <param name="sNamespace">Namespace</param>
		/// <param name="idhFiles">Names of IDH file used to retrieve comments.</param>
		/// <param name="referencedFiles">Names of files used to resolve references to
		/// external types.</param>
		/// <param name="fCreateComments"><c>true</c> to create XML comments</param>
		/// <returns><c>true</c> if import successful, otherwise <c>false</c>.</returns>
		/// ------------------------------------------------------------------------------------
		public bool Import(List<string> usingNamespaces, string sFileName, string sXmlFile,
			string sOutFile, string sNamespace, StringCollection idhFiles,
			StringCollection referencedFiles, bool fCreateComments)
		{
			var fOk = true;
			var codeNamespace = new CodeNamespace();

			// Add additional using statements
			foreach (var ns in usingNamespaces)
				codeNamespace.Imports.Add(new CodeNamespaceImport(ns));

			// Add types from referenced files so that we can resolve types that are not
			// defined in this IDL file.
			foreach (var refFile in referencedFiles)
			{
				var referencedNamespace = DeserializeData(refFile);
				if (referencedNamespace == null)
					continue;

				foreach (string key in referencedNamespace.UserData.Keys)
					codeNamespace.UserData[key] = referencedNamespace.UserData[key];
			}

			// Load the IDL conversion rules
			if (sXmlFile == null)
			{
				var assembly = Assembly.GetExecutingAssembly();
				sXmlFile = Path.ChangeExtension(assembly.Location, "xml");
			}
			var conversions = IDLConversions.Deserialize(sXmlFile);
			conversions.Namespace = codeNamespace;

#if SINGLE_THREADED
			ParseIdhFiles(idhFiles);
#else
			_waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
			ThreadPool.QueueUserWorkItem(ParseIdhFiles, idhFiles);
#endif

			using (var stream = new FileStream(sFileName, FileMode.Open, FileAccess.Read))
			{
				var lexer = new IDLLexer(stream);
				var parser = new IDLParser(lexer);
				parser.setFilename(sFileName);

				codeNamespace.Name = sNamespace;
				codeNamespace.Comments.AddRange(AddFileBanner(sFileName, sOutFile));
				codeNamespace.Imports.Add(new CodeNamespaceImport("System"));
				codeNamespace.Imports.Add(new CodeNamespaceImport("System.Runtime.InteropServices"));
				codeNamespace.Imports.Add(new CodeNamespaceImport("System.Runtime.InteropServices.ComTypes"));
				codeNamespace.Imports.Add(new CodeNamespaceImport("System.Runtime.CompilerServices"));

				// And now parse the IDL file
				parser.specification(codeNamespace, conversions);

				// Merge properties
				fOk = MergeProperties(codeNamespace);

				IDLConversions.AdjustReferencesInEnums();

				// Add XML comments
				if (fCreateComments)
				{
					_waitHandle?.WaitOne();

					AddComments(codeNamespace.Types);
				}

				// Serialize what we have so that we can re-use later if necessary
				SerializeData(sFileName, codeNamespace);

				// Finally, create the source code
				GenerateCode(sOutFile, codeNamespace);
			}

			return fOk;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Parses the idh files. This is done on a background thread.
		/// </summary>
		/// <param name="obj">List of IDH file names.</param>
		/// ------------------------------------------------------------------------------------
		private void ParseIdhFiles(object obj)
		{
			var idhFiles = obj as StringCollection;

			// Create IDH processor that will provide the comments from the IDH file
			s_idhProcessor = new IdhCommentProcessor(idhFiles);

			_waitHandle?.Set();
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Merges two properties with the same name into one with a get and set method.
		/// </summary>
		/// <param name="codeNamespace">The namespace that defines all classes and methods.</param>
		/// <returns><c>false</c> if a method marked with [propget] comes after a method
		/// with the same name marked as [propput] or [propputref], otherwise <c>true</c>.</returns>
		/// ------------------------------------------------------------------------------------
		private bool MergeProperties(CodeNamespace codeNamespace)
		{
			var fOk = true;
			foreach (CodeTypeDeclaration type in codeNamespace.Types)
			{
				for (var i = 0; i < type.Members.Count; i++)
				{
					for (var j = i + 1; j < type.Members.Count; j++)
					{
						if (type.Members[i].Name != type.Members[j].Name ||
							!(type.Members[i] is CodeMemberProperty) ||
							!(type.Members[j] is CodeMemberProperty))
						{
							continue;
						}

						var first = type.Members[i] as CodeMemberProperty;
						var second = type.Members[j] as CodeMemberProperty;
						if (first == null || second == null)
							continue;

						if (second.HasSet)
						{
							first.HasSet = second.HasSet;
							if (second.UserData.Contains("set_attrs"))
								first.UserData["set_attrs"] = second.UserData["set_attrs"];
						}
						if (second.HasGet)
						{
							// Get needs to come first
							Logger.Error($"Error: [propget] after [propput/propputref] in {type.Name}.{first.Name}. " +
										"For properties to work in .NET [propget] needs to be defined before [propput].");
							fOk = false;

							first.HasGet = second.HasGet;
							if (second.UserData.Contains("get_attrs"))
								first.UserData["get_attrs"] = second.UserData["get_attrs"];
						}
						type.Members.Remove(second);
					}
				}
			}
			return fOk;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Serializes the data to a file with the same name as the IDL file but with the
		/// ".json" extension.
		/// </summary>
		/// <param name="fileName">Name of the IDL file.</param>
		/// <param name="cnamespace">The namespace definition with all classes and methods.</param>
		/// ------------------------------------------------------------------------------------
		private void SerializeData(string fileName, CodeNamespace cnamespace)
		{
			var serializedDefinition = JsonConvert.SerializeObject(cnamespace,
				/* Formatting.Indented, */
				new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
			File.WriteAllText(Path.ChangeExtension(fileName, "json"), serializedDefinition);
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Deserializes the data.
		/// </summary>
		/// <param name="fileName">Name of the JSON file.</param>
		/// <returns>The namespace definition with all classes and methods.</returns>
		/// ------------------------------------------------------------------------------------
		private CodeNamespace DeserializeData(string fileName)
		{
			if (!File.Exists(fileName))
				return null;

			try
			{
				var serializedData = File.ReadAllText(fileName);
				return JsonConvert.DeserializeObject<CodeNamespace>(serializedData,
					new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
			}
			catch (Exception e)
			{
				Logger.Error($"Failed to deserialize referenced data from file \"{fileName}\". Reason: {e.Message}");
			}
			return null;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Generates the code.
		/// </summary>
		/// <param name="sOutFile">The name of the output file (C# file).</param>
		/// <param name="cnamespace">The namespace definition with all classes and methods.</param>
		/// ------------------------------------------------------------------------------------
		private static void GenerateCode(string sOutFile, CodeNamespace cnamespace)
		{
			using (TextWriter textWriter = new StreamWriter(new FileStream(sOutFile, FileMode.Create)))
			{
				var cgo = new CodeGeneratorOptions {
					BracingStyle = "C",
					IndentString = "\t",
					VerbatimOrder = true
				};

				CodeDomProvider codeProvider = new CSharpCodeProviderEx();
				codeProvider.GenerateCodeFromNamespace(cnamespace, textWriter, cgo);
			}
		}
	}
}
