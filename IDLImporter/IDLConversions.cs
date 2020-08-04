// Copyright (c) 2002-2015 SIL International
// This software is licensed under the LGPL, version 2.1 or later
// (http://www.gnu.org/licenses/lgpl-2.1.html)

//#define DEBUG_IDLGRAMMAR

using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;

namespace SIL.IdlImporterTool
{
	#region XML type definition
	[System.Xml.Serialization.XmlTypeAttribute(Namespace="http://dummy.sil.org/IDLConversions.xsd")]
	// ReSharper disable once ClassNeverInstantiated.Global
	public class ConversionEntry
	{
		public ConversionEntry()
		{}

		public ConversionEntry(string match, string replace)
		{
			Match = match;
			Replace = replace;
		}

		private string m_sAttrValue;
		private string m_sMatch;
		private string m_sNewAttrValue;

		public string Attribute { get; set; }

		[XmlIgnore]
		public string[] Attributes => Attribute?.Split(',');

		[XmlIgnore]
		public string AttrValueName { get; private set; }

		public string AttrValue
		{
			get => m_sAttrValue;
			set
			{
				var parts = value.Split('=');
				if (parts.Length > 1)
				{
					AttrValueName = parts[0];
					m_sAttrValue = parts[parts.Length-1];
				}
				else
					m_sAttrValue = value;
			}
		}

		public string Match
		{
			get => m_sMatch;
			set
			{
				m_sMatch = value;
				Regex = new Regex(m_sMatch);
			}
		}

		public string Replace { get; set; }

		public string NewAttribute { get; set; }

		[XmlIgnore]
		public string[] NewAttributes => NewAttribute?.Split(',');

		[XmlIgnore]
		public string NewAttrValueName { get; private set; }

		public string NewAttrValue
		{
			get => m_sNewAttrValue;
			set
			{
				var parts = value.Split('=');
				if (parts.Length > 1)
				{
					NewAttrValueName = parts[0];
					m_sNewAttrValue = parts[parts.Length-1];
				}
				else
					m_sNewAttrValue = value;
			}
		}

		public bool fEnd { get; set; } = true;

		[XmlIgnore]
		public Regex Regex { get; private set; }
	}
	#endregion

	/// ----------------------------------------------------------------------------------------
	/// <summary>
	/// Defines most of the conversions that will be performed on the interfaces of the IDL file.
	/// </summary>
	/// ----------------------------------------------------------------------------------------
	[System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://dummy.sil.org/IDLConversions.xsd")]
	[System.Xml.Serialization.XmlRootAttribute(Namespace="http://dummy.sil.org/IDLConversions.xsd",
		IsNullable=false)]
	// ReSharper disable once InconsistentNaming
	public class IDLConversions
	{
		#region Serialization
		public void Serialize(string fileName)
		{
			TextWriter textWriter = null;

			try
			{
				textWriter = new StreamWriter(fileName);
				var xmlSerializer = new XmlSerializer(typeof(IDLConversions));
				xmlSerializer.Serialize(textWriter, this);
			}
			finally
			{
				textWriter?.Close();
			}
		}

		public static IDLConversions Deserialize(string fileName)
		{
			IDLConversions ret = null;
			using (var reader = new XmlTextReader(fileName))
			{
				var xmlSerializer = new XmlSerializer(typeof(IDLConversions));
				try
				{
					ret = (IDLConversions)xmlSerializer.Deserialize(reader);
				}
				catch (InvalidOperationException e)
				{
					throw new InvalidOperationException($"Deserializing {fileName} failed: {e.Message}", e);
				}

				if (ret.m_ParamNames == null)
					return ret;

				s_ParamNames = new ConversionEntry[ret.m_ParamNames.Length];
				ret.m_ParamNames.CopyTo(s_ParamNames, 0);
			}

			return ret;
		}
		#endregion

		#region Variables
		[System.Xml.Serialization.XmlElementAttribute("ParamTypeConversion")]
		public ConversionEntry[] m_ParamTypes;

		[System.Xml.Serialization.XmlElementAttribute("ParamNameConversion")]
		public ConversionEntry[] m_ParamNames; // only here so that we can serialize it.

		private static ConversionEntry[] s_ParamNames;
		private static Dictionary<CodeFieldReferenceExpression, string> s_NeedsAdjustment =
			new Dictionary<CodeFieldReferenceExpression, string>();
		private static Dictionary<string, string> s_EnumMemberMapping =
			new Dictionary<string, string>();
		#endregion

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Gets or sets the namespace.
		/// </summary>
		/// <value>The namespace.</value>
		/// ------------------------------------------------------------------------------------
		[XmlIgnore]
		public CodeNamespace Namespace { get; set; }

		#region General conversion methods
		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Handle the function declaration, i.e. look at the attributes and change the return
		/// value and other stuff
		/// </summary>
		/// <param name="member">Contains information about the function</param>
		/// <param name="rt">Specified return value</param>
		/// <param name="types">The types.</param>
		/// <param name="attributes">The attributes.</param>
		/// <returns>Function or property description</returns>
		/// ------------------------------------------------------------------------------------
		public CodeTypeMember HandleFunction_dcl(CodeMemberMethod member, CodeTypeReference rt,
			CodeTypeMemberCollection types, Hashtable attributes)
		{
			var fPreserveSig = false;
			CodeTypeMember memberRet = member;

			if (attributes["custom"] != null)
			{
				var arg = (CodeAttributeArgument)attributes["custom"];
				if (arg.Name == "842883D3-DC67-45cf-B968-E763D37A7A19")
				{
					if (arg.Value != null && arg.Value.ToString() != "false")
					{	// preserve signature
						fPreserveSig = true;
						member.ReturnType = rt;
						memberRet.CustomAttributes.Add(new CodeAttributeDeclaration("PreserveSig"));
					}

					attributes.Remove("custom");
				}
			}

			if (attributes["propget"] != null || attributes["propput"] != null || attributes["propputref"] != null)
			{
				if (member.Parameters.Count == 1)
				{
					// normal property - deal with it the .NET way (get/set)
					var property = new CodeMemberProperty();
					property.Attributes = memberRet.Attributes;
					property.Comments.AddRange(memberRet.Comments);
					property.CustomAttributes.AddRange(memberRet.CustomAttributes);
					property.EndDirectives.AddRange(memberRet.EndDirectives);
					property.LinePragma = memberRet.LinePragma;
					property.Name = memberRet.Name;
					property.StartDirectives.AddRange(memberRet.StartDirectives);
					foreach (var key in memberRet.UserData.Keys)
						property.UserData.Add(key, memberRet.UserData[key]);

					if (attributes["propget"] != null)
					{
						property.HasGet = true;
						attributes.Remove("propget");
					}
					if (attributes["propput"] != null || attributes["propputref"] != null)
					{
						property.HasSet = true;
						Trace.Assert(attributes["propput"] == null || attributes["propputref"] == null);
						attributes.Remove("propput");
						attributes.Remove("propputref");
					}
					property.Type = member.Parameters[0].Type;

					// If the single Parameter has had a MarshalAs CustomAttribute added to it then stores
					// this in the propertys User Data.
					if (member.Parameters[0].CustomAttributes.Count > 0)
					{
						foreach(CodeAttributeDeclaration codeAttribDec in member.Parameters[0].CustomAttributes)
						{
							if (codeAttribDec.Name.Contains("MarshalAs"))
							{
								property.UserData["MarshalAsType"] = ((CodeSnippetExpression)codeAttribDec.Arguments[0].Value).Value;
							}
						}

						// property.UserData["MarshalAsType"] = ((CodeSnippetExpression)member.Parameters[0].CustomAttributes[0].Arguments[0].Value).Value; // todo
					}
					else
					{
						// No explicit Custom Marshaler has been set - However we need to marshal it anyway if its a COM interface
						if (IsTypeIDLInterface(member.Parameters[0].Type.BaseType))
						{
							property.UserData["MarshalAsType"] = "UnmanagedType.Interface";
						}
					}
					memberRet = property;
				}
				else
				{
					// parameter with multiple parameters - can't use get/set the .NET way
					if (attributes["propget"] != null)
					{
						memberRet.Name = "get_" + memberRet.Name;
						attributes.Remove("propget");
					}
					if (attributes["propput"] != null)
					{
						var iPropSet = IndexOfMember(types, "set_" + member.Name);
						if (iPropSet > -1)
							memberRet.Name = "let_" + memberRet.Name;
						else
							memberRet.Name = "set_" + memberRet.Name;
						attributes.Remove("propput");
					}
					if (attributes["propputref"] != null)
					{
						var iPropSet = IndexOfMember(types, "set_" + member.Name);
						if (iPropSet > -1)
							memberRet.Name = "let_" + memberRet.Name;
						else
							memberRet.Name = "set_" + memberRet.Name;
						attributes.Remove("propputref");
					}
				}
			}

			if (!fPreserveSig)
			{
				var retParam = GetReturnType(member.Parameters);
				member.ReturnType = retParam.Type;
				member.CustomAttributes.AddRange(retParam.CustomAttributes);
				for (var i = 0; i < member.CustomAttributes.Count; i++)
				{
					member.CustomAttributes[i].Name = "return: " + member.CustomAttributes[i].Name;
				}
			}


			// Needs to come after putting "return:" in front!
			if (attributes["local"] != null)
			{
				member.CustomAttributes.Add(new CodeAttributeDeclaration("Obsolete",
					new CodeAttributeArgument(new CodePrimitiveExpression(
					"Can't call COM method marked with [local] attribute in IDL file"))));
				attributes.Remove("local");
			}
			if (attributes["restricted"] != null)
			{
				member.CustomAttributes.Add(new CodeAttributeDeclaration("TypeLibFunc",
					new CodeAttributeArgument(new CodeSnippetExpression("TypeLibFuncFlags.FRestricted"))));
				attributes.Remove("restricted");
			}
			if (attributes["warning"] != null)
			{
				member.Comments.Add(new CodeCommentStatement(string.Format("<remarks>{0}</remarks>",
					(string)attributes["warning"]), true));
				attributes.Remove("warning");
			}

			// Add the attributes
			foreach (DictionaryEntry entry in attributes)
			{
				if (entry.Value is CodeAttributeArgument value)
					memberRet.CustomAttributes.Add(new CodeAttributeDeclaration((string)entry.Key,
						value));
				else
					memberRet.CustomAttributes.Add(new CodeAttributeDeclaration((string)entry.Key));
			}
			attributes.Clear();

			AddMethodImplAttr(memberRet, true);

			return memberRet;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Gets the return type.
		/// </summary>
		/// <param name="parameters">The parameters.</param>
		/// <returns></returns>
		/// ------------------------------------------------------------------------------------
		private CodeParameterDeclarationExpression GetReturnType(
			CodeParameterDeclarationExpressionCollection parameters)
		{
			var retType = new CodeParameterDeclarationExpression(typeof(void),
				"return");
			foreach (CodeParameterDeclarationExpression exp in parameters)
			{
				if (exp.UserData["retval"] != null && (bool)exp.UserData["retval"] &&
					exp.Type.ArrayRank <=0)
				{	// Marshalling arrays as return value doesn't work!
					retType = exp;
					parameters.Remove(exp);
					break;
				}
			}

			return retType;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Gets the index of a member method/property by name
		/// </summary>
		/// <param name="types">member collection</param>
		/// <param name="name">name to look for</param>
		/// <returns>index of member if found, otherwise -1</returns>
		/// ------------------------------------------------------------------------------------
		private int IndexOfMember(CodeTypeMemberCollection types, string name)
		{
			foreach (CodeTypeMember member in types)
			{
				if (member.Name == name)
					return types.IndexOf(member);
			}

			return -1;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Handles all base classes. For IUnknown and IDispatch we set an attribute
		/// instead of adding it to the list of base clases
		/// </summary>
		/// <param name="type">Type description</param>
		/// <param name="nameSpace">The name space.</param>
		/// <param name="attributes">The attributes.</param>
		/// ------------------------------------------------------------------------------------
		public void HandleInterface(CodeTypeDeclaration type, CodeNamespace nameSpace,
			IDictionary attributes)
		{
			type.CustomAttributes.Add(new CodeAttributeDeclaration("ComImport"));

			var toRemove = new CodeAttributeDeclarationCollection();

			if (attributes["dual"] != null)
			{
				type.UserData.Add("InterfaceType", "ComInterfaceType.InterfaceIsDual");
				attributes.Remove("dual");
			}

			if (attributes["Guid"] != null)
				Namespace.UserData.Add(type.Name + "Guid", attributes["Guid"]);

			var superClasses = (StringCollection)type.UserData["inherits"];
			// Prepare to remove redundant superclasses
			var allBases = new Dictionary<string, StringCollection>();
			foreach (var str in superClasses)
				allBases[str] = AllBases(str, nameSpace);
			foreach (var str in superClasses)
			{
				switch (str)
				{
					case "IUnknown":
					case "IDispatch":
						if (type.UserData["InterfaceType"] != null)
						{	// we had a interface spec previously
							type.UserData["InterfaceType"] = "ComInterfaceType.InterfaceIsDual";
						}
						else
							type.UserData.Add("InterfaceType", "ComInterfaceType.InterfaceIs" + str);
						break;
					default:
					{
						if (type.BaseTypes.Count > 0)
						{
							Console.WriteLine("Error: only one base class supported (interface {0})!",
								type.Name);
						}
						else
						{
							var fRedundant = false;
							foreach (var other in superClasses)
							{
								// Is this base class contained in another?
								if (other != str && allBases[other].Contains(str))
								{
									fRedundant = true;
									break;
								}
							}
							if (fRedundant)
								break;

							string interfaceType;
							var tmpColl = GetBaseMembers(str, nameSpace,
								out interfaceType);
							if (tmpColl != null)
							{
								tmpColl.AddRange(type.Members);
								type.Members.Clear();
								type.Members.AddRange(tmpColl);
								if (type.UserData["InterfaceType"] == null)
									type.UserData.Add("InterfaceType", interfaceType);
							}
							type.BaseTypes.Add(new CodeTypeReference(str));
						}
						break;
					}
				}
			}

			if (type.UserData["InterfaceType"] != null)
			{
				if ((string)type.UserData["InterfaceType"] == "ComInterfaceType.InterfaceIsDual")
					type.UserData.Remove("InterfaceType");
				else
					type.CustomAttributes.Add(new CodeAttributeDeclaration("InterfaceType",
						new CodeAttributeArgument(new CodeSnippetExpression(
						(string)type.UserData["InterfaceType"]))));
			}

			foreach (CodeAttributeDeclaration attr in toRemove)
				type.CustomAttributes.Remove(attr);

			AddAttributesToType(type, attributes);
			attributes.Clear();

#if DEBUG_IDLGRAMMAR
			foreach (CodeTypeMember member in type.Members)
			{
				System.Diagnostics.Debug.WriteLine(string.Format("member={0}.{1}", type.Name,
					member.Name));
			}
#endif
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Gets alls base class names.
		/// </summary>
		/// <param name="typeName">Name of the type.</param>
		/// <param name="nameSpace">The namespace.</param>
		/// <returns></returns>
		/// ------------------------------------------------------------------------------------
		private StringCollection AllBases(string typeName, CodeNamespace nameSpace)
		{
			var type = (CodeTypeDeclaration)nameSpace.UserData[typeName];
			if (type == null)
			{
				//System.Console.WriteLine("Type missing for {0}", typeName);
				return new StringCollection();
			}
			var directBases = (StringCollection)type.UserData["inherits"];
			if (directBases == null)
			{
				//System.Console.WriteLine("Bases missing for {0}", typeName);
				return new StringCollection();
			}
			var result = new StringCollection();
			// This is astonishingly ugly, but I couldn't easily find a better way to do it
			var tmp = new string[directBases.Count];
			directBases.CopyTo(tmp, 0);
			result.AddRange(tmp);
			foreach (var @base in directBases)
			{
				var theseBases = AllBases(@base, nameSpace);
				tmp = new string[theseBases.Count];
				theseBases.CopyTo(tmp, 0);
				result.AddRange(tmp);
			}
			return result;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// returns true if given type is a COM interface defined/reference in the IDL file.
		/// </summary>
		/// <param name="typeName">Name of the type.</param>
		/// <returns>boolean</returns>
		/// ------------------------------------------------------------------------------------
		public bool IsTypeIDLInterface(string typeName)
		{
			var rv = (AllBases(typeName, this.Namespace).Count > 0);

			if (rv == false)
			{
				rv = ((Namespace.UserData[typeName] != null)
					  && ((CodeTypeDeclaration)Namespace.UserData[typeName]).IsInterface);
			}

			return rv;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Adds the type of the attributes to.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="attributes">The attributes.</param>
		/// ------------------------------------------------------------------------------------
		private static void AddAttributesToType(CodeTypeDeclaration type, IDictionary attributes)
		{
			// Add the attributes
			foreach (DictionaryEntry entry in attributes)
			{
				if (entry.Value is CodeAttributeArgument value)
					type.CustomAttributes.Add(new CodeAttributeDeclaration((string)entry.Key,
						value));
				else
					type.CustomAttributes.Add(new CodeAttributeDeclaration((string)entry.Key));
			}
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Handles the co class interface.
		/// </summary>
		/// <param name="type">The type (coClass interface declaration).</param>
		/// <param name="nameSpace">The name space.</param>
		/// <param name="attributes">The attributes.</param>
		/// ------------------------------------------------------------------------------------
		public void HandleCoClassInterface(CodeTypeDeclaration type, CodeNamespace nameSpace,
			IDictionary attributes)
		{
			// Add a start region
			type.StartDirectives.Add(new CodeRegionDirective(CodeRegionMode.Start,
				type.Name + " CoClass definitions"));

			type.CustomAttributes.Add(new CodeAttributeDeclaration("ComImport"));
			type.CustomAttributes.Add(new CodeAttributeDeclaration("CoClass",
				new CodeAttributeArgument(new CodeTypeOfExpression(GetCoClassObjectName(type)))));

			// we have to change the GUID: the interface we're defining here is just a synonym
			// for the first interface this coclass implements. This means we have to replace
			// the GUID with the GUID of the first interface (i.e. base class).
			var attributesCopy = new Hashtable(attributes);
			var guid =
				Namespace.UserData[type.BaseTypes[0].BaseType + "Guid"] as CodeAttributeArgument;
			attributesCopy["Guid"] = guid;

			AddAttributesToType(type, attributesCopy);

			// ignore the IMarshal interface
			for (var i = 0; i < type.BaseTypes.Count; i++)
			{
				if (type.BaseTypes[i].BaseType == "IMarshal")
				{
					type.BaseTypes.RemoveAt(i);
					break;
				}
			}
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Add a MethodImpl attribute to a method member.
		/// </summary>
		/// <param name="member">The method member.</param>
		/// <param name="fInterface"><c>true</c> if we currently deal with an interface
		/// declaration, <c>false</c> if we deal with the actual implementation of the
		/// interface.</param>
		/// ------------------------------------------------------------------------------------
		public void AddMethodImplAttr(CodeTypeMember member, bool fInterface)
		{
			// REVIEW: Is InternalCall the right thing to do? MSDN doc says that InternalCall
			// is to be used for methods implemented in the CLR. Would unmanaged be better?
			// When we put [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType=MethodCodeType.Runtime)]
			// on all interface methods and properties we get a crash in tests when
			// calling ISilDataAccess.get_GuidProp on Linux 64-bit.
			var methodImplAttr =
				new CodeAttributeDeclaration("MethodImpl",
					new CodeAttributeArgument(
					new CodeSnippetExpression("MethodImplOptions.InternalCall")),
					new CodeAttributeArgument("MethodCodeType",
					new CodeSnippetExpression("MethodCodeType.Runtime")));

			if (member is CodeMemberProperty prop)
			{
				// for a property the attribute must be on the get/set, not on
				// the enclosing property. The default C# code generator doesn't
				// support this, so we handle this in our custom code generator.
				var attrColl =
					new CodeAttributeDeclarationCollection(
					new CodeAttributeDeclaration[] { methodImplAttr });
				if (prop.HasGet)
					prop.UserData["get_attrs"] = attrColl;
				if (prop.HasSet)
					prop.UserData["set_attrs"] = attrColl;
			}
			else
			{
				if (member is CodeMemberMethod method && fInterface)
				{
					if (method.ReturnType.BaseType == typeof(Guid).ToString())
					{
						// Don't set the MethodImpl attribute on methods that return
						// a Guid. This caused crashes on Linux 64-bit when running tests
						// (and maybe in real live as well;-)
						return;
					}
				}

				if (member.UserData["MethodImpl"] == null)
				{
					member.CustomAttributes.Add(methodImplAttr);
					member.UserData["MethodImpl"] = true;
				}
			}
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Declares the co class object.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="nameSpace">The name space.</param>
		/// <param name="attributes">The attributes.</param>
		/// <returns>The type declaration for the CoClass object. The CoClass object gets
		/// named after the interface it implements with a leading underscore and "Class"
		/// appended.</returns>
		/// ------------------------------------------------------------------------------------
		public CodeTypeDeclaration DeclareCoClassObject(CodeTypeDeclaration type,
			CodeNamespace nameSpace, IDictionary attributes)
		{
			var coClassName = GetCoClassObjectName(type);
			var coClass = new CodeTypeDeclaration(coClassName)
			{
				IsClass = true, TypeAttributes = TypeAttributes.NestedAssembly
			};
			coClass.CustomAttributes.Add(new CodeAttributeDeclaration("ComImport"));
			coClass.CustomAttributes.Add(new CodeAttributeDeclaration("ClassInterface",
				new CodeAttributeArgument(new CodeSnippetExpression("ClassInterfaceType.None"))));
			coClass.CustomAttributes.Add(new CodeAttributeDeclaration("TypeLibType",
				new CodeAttributeArgument(new CodeSnippetExpression("TypeLibTypeFlags.FCanCreate"))));
			coClass.BaseTypes.Add(type.BaseTypes[0]);
			coClass.BaseTypes.Add(new CodeTypeReference(type.Name));

			// Prepare to remove redundant superclasses
			var bases = new CodeTypeReferenceCollection();
			var allBases = new Dictionary<string, StringCollection>();
			foreach (CodeTypeReference baseType in type.BaseTypes)
				allBases[baseType.BaseType] = AllBases(baseType.BaseType, nameSpace);
			foreach (CodeTypeReference baseType in type.BaseTypes)
			{
				var fRedundant = false;
				foreach (CodeTypeReference other in type.BaseTypes)
				{
					// Is this base class contained in another?
					if (other != baseType && allBases[other.BaseType].Contains(baseType.BaseType))
					{
						fRedundant = true;
						break;
					}
				}
				if (!fRedundant)
					bases.Add(baseType);
			}

			foreach (CodeTypeReference baseType in bases)
			{
				var tmpColl = GetBaseMembers(baseType.BaseType, nameSpace,
					out _);
				if (tmpColl == null)
					continue;

				// adjust attributes
				foreach (CodeTypeMember member in tmpColl)
				{
					//member.Attributes &= ~MemberAttributes.New;
					member.Attributes = MemberAttributes.Public;
					member.UserData.Add("extern", true);

					AddMethodImplAttr(member, false);
				}

				tmpColl.AddRange(coClass.Members);
				coClass.Members.Clear();
				coClass.Members.AddRange(tmpColl);
			}

			// Add a region around this class
			coClass.StartDirectives.Add(new CodeRegionDirective(CodeRegionMode.Start,
				"Private " + coClassName + " class"));
			coClass.EndDirectives.Add(new CodeRegionDirective(CodeRegionMode.End, coClassName));

			AddAttributesToType(coClass, attributes);

			return coClass;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Declares the coclass creator class.
		/// </summary>
		/// <param name="type">The type name.</param>
		/// <param name="nameSpace">The name space.</param>
		/// <param name="attributes">The attributes.</param>
		/// <returns>coclass creator class declaration.</returns>
		/// ------------------------------------------------------------------------------------
		public CodeTypeDeclaration DeclareCoClassCreator(CodeTypeDeclaration type,
			CodeNamespace nameSpace, IDictionary attributes)
		{
			var coClassCreator = new CodeTypeDeclaration(type.Name + "Class")
			{
				TypeAttributes = TypeAttributes.Public, Attributes = MemberAttributes.Static
			};

			// .NET 2.0 allows static classes, but unfortunately the C# code generator
			// doesn't have a way to generate code that way directly yet, so we add a userdata
			// property and deal with that in our custom code generator.
			coClassCreator.UserData.Add("static", true);

			var returnType = new CodeTypeReference(type.Name);

			// add the Create() method declaration
			var createMethod = new CodeMemberMethod
			{
				Attributes = MemberAttributes.Static | MemberAttributes.Public,
				Name = "Create",
				ReturnType = returnType
			};

			createMethod.Statements.Add(new CodeMethodReturnStatement(
				new CodeObjectCreateExpression(GetCoClassObjectName(type))));

			coClassCreator.Members.Add(createMethod);

			coClassCreator.EndDirectives.Add(new CodeRegionDirective(CodeRegionMode.End, string.Empty));

			var childMethods =
				new Dictionary<string, IdhCommentProcessor.CommentInfo>();

			childMethods.Add("Create", new IdhCommentProcessor.CommentInfo("Creates a new " +
				type.Name + " object", null, 0));
			IDLImporter.s_MoreComments.Add(coClassCreator.Name,
				new IdhCommentProcessor.CommentInfo("Helper class used to create a new instance of the "
				+ type.Name + " COM object", childMethods, 0));
			return coClassCreator;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Gets the name of the co class object.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <returns></returns>
		/// ------------------------------------------------------------------------------------
		private static string GetCoClassObjectName(CodeTypeDeclaration type)
		{
			return $"_{type.Name}Class";
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Returns a copy of the collection of the methods of the base type
		/// </summary>
		/// <param name="typeName">The name of the base type</param>
		/// <param name="nameSpace">The namespace which defines the base types</param>
		/// <param name="interfaceType">[out] Returns the interface type of the base type</param>
		/// <returns>A copy of the collection of the methods</returns>
		/// ------------------------------------------------------------------------------------
		private CodeTypeMemberCollection GetBaseMembers(string typeName,
			CodeNamespace nameSpace, out string interfaceType)
		{
			interfaceType = null;
			if (nameSpace.UserData[typeName] == null)
			{
				System.Console.WriteLine("Error: base type {0} not found!", typeName);
				return null;
			}
			else
			{
				var type = (CodeTypeDeclaration)nameSpace.UserData[typeName];
				interfaceType = (string)type.UserData["InterfaceType"];
				var coll = new CodeTypeMemberCollection();

				// All base class members must be preceded by new
				foreach (CodeTypeMember member in type.Members)
				{
					CodeTypeMember newMember;
					switch (member)
					{
						case CodeMemberMethod _:
							newMember = new CodeMemberMethod();
							break;
						case CodeMemberProperty _:
							newMember = new CodeMemberProperty();
							break;
						default:
							Console.WriteLine("Unhandled member type: {0}", member.GetType());
							continue;
					}

					newMember.Attributes = member.Attributes | MemberAttributes.New;
					newMember.Attributes = newMember.Attributes & ~MemberAttributes.AccessMask |
						MemberAttributes.Public;
					newMember.Comments.AddRange(member.Comments);
					newMember.CustomAttributes.AddRange(member.CustomAttributes);
					newMember.Name = member.Name;
					switch (member)
					{
						case CodeMemberMethod method:
							((CodeMemberMethod)newMember).ImplementationTypes.AddRange(method.ImplementationTypes);
							((CodeMemberMethod)newMember).Parameters.AddRange(method.Parameters);
							((CodeMemberMethod)newMember).ReturnType = method.ReturnType;
							((CodeMemberMethod)newMember).ReturnTypeCustomAttributes.AddRange(method.ReturnTypeCustomAttributes);
							break;
						case CodeMemberProperty property:
							((CodeMemberProperty)newMember).ImplementationTypes.AddRange(property.ImplementationTypes);
							((CodeMemberProperty)newMember).Type = property.Type;
							((CodeMemberProperty)newMember).HasGet = property.HasGet;
							((CodeMemberProperty)newMember).HasSet = property.HasSet;
							break;
					}
					foreach (DictionaryEntry entry in member.UserData)
						newMember.UserData.Add(entry.Key, entry.Value);

					coll.Add(newMember);
				}

				return coll;
			}
		}

		#endregion

		#region Conversions based on XML configuration file
		/// <summary>
		/// Make conversions based on attributes
		/// </summary>
		/// <param name="type">Type of parameter</param>
		/// <param name="sParameter">original parameter string</param>
		/// <param name="param">parameter description</param>
		/// <returns></returns>
		public CodeTypeReference ConvertParamType(string sOriginalParameter,
			CodeParameterDeclarationExpression param, IDictionary attributes)
		{
			var type = new CodeTypeReference(string.Empty);
			var sParameter = sOriginalParameter;

			if (m_ParamTypes != null)
			{
				foreach (var entry in m_ParamTypes)
				{
					if (!entry.Regex.IsMatch(sParameter))
						continue;

					var fMatch = false;
					if (entry.Attributes != null)
					{
						var iAttribute = 0;
						foreach(var attr in entry.Attributes)
						{
							iAttribute++;
							var attribute = attr.Trim();
							var fNegate = false;
							if (attribute[0] == '~')
							{
								fNegate = true;
								attribute = attribute.Substring(1);
							}

							if (attributes[attribute] != null)
							{
								var rawArg = attributes[attribute];
								CodeAttributeArgument arg;

								if (rawArg.GetType() != typeof(CodeAttributeArgument))
									arg = new CodeAttributeArgument(new CodePrimitiveExpression(rawArg));
								else
									arg = (CodeAttributeArgument)rawArg;

								// We test the attribute value only for the first attribute!
								if (entry.AttrValue != null &&
									(iAttribute > 1 || entry.AttrValue != (string)((CodePrimitiveExpression)arg.Value).Value))
									continue;

								if (entry.AttrValueName != null && entry.AttrValueName != arg.Name)
									continue;

								if (!fNegate && (iAttribute <= 1 || fMatch == true))
									fMatch = true;
								else
								{
									fMatch = false;
									break;
								}
							}
							else if (fNegate && (iAttribute <= 1 || fMatch == true))
							{ // attribute not found, and we don't want to have it, so it's a match
								fMatch = true;
							}
							else
							{
								fMatch = false;
								break;
							}
						}
					}
					else
						fMatch = true;

					if (!fMatch)
						continue;

					{
						sParameter = entry.Regex.Replace(sParameter, entry.Replace);

						if (entry.NewAttributes != null)
						{
							var iAttribute = 0;
							foreach(var attr in entry.NewAttributes)
							{
								iAttribute++;
								var attribute = attr.Trim();

								if (attribute[0] == '-')
								{
									attributes.Remove(attribute.Substring(1));
								}
								else if (iAttribute == 1 && param != null)
								{
									// we only deal with one attribute to add
									if (entry.NewAttrValue == null)
									{
										// attribute without value
										param.CustomAttributes.Add(new CodeAttributeDeclaration(
											attribute));
									}
									else
									{
										// attribute with value
										CodeAttributeArgument arg;
										if (entry.NewAttrValueName == null)
										{
											// attribute with unnamed value
											arg = new CodeAttributeArgument(
												new CodeSnippetExpression(entry.NewAttrValue));
										}
										else
										{
											// attribute with named value
											arg = new CodeAttributeArgument(entry.NewAttrValueName,
												new CodeSnippetExpression(entry.NewAttrValue));
										}

										param.CustomAttributes.Add(new CodeAttributeDeclaration(
											attribute, arg));
									}
								}
							}
						}

						if (entry.fEnd)
							break;
					}
				}
			}

			// Remove the parameter name from the end
			var regex = new Regex("\\s+[^\\s]+[^\\w]*$");
			type.BaseType = regex.Replace(sParameter.TrimStart(null), "");

			var regexArray = new Regex("\\[\\s*\\]\\s*$");
			if (regexArray.IsMatch(type.BaseType))
			{
				type.BaseType = regexArray.Replace(type.BaseType, "");
				var tmpType = new CodeTypeReference(type, 1);
				type = tmpType;
			}

			if (param != null)
				HandleInOut(param, attributes);

			// Put size_is to UserData so that we can deal with it later when we have all
			// parameters
			var varName = (string)attributes["size_is"];
			if (!string.IsNullOrEmpty(varName))
				param?.UserData.Add("size_is", varName);
			attributes.Remove("size_is");
			attributes.Remove("string");

			if (attributes["retval"] != null)
			{
				param?.UserData.Add("retval", attributes["retval"]);
				attributes.Remove("retval");
			}

			if (attributes["IsArray"] != null)
				attributes.Remove("IsArray");

			// Add the attributes
			if (param != null)
			{
				foreach (DictionaryEntry entry in attributes)
				{
					if (entry.Value is CodeAttributeArgument value)
						param.CustomAttributes.Add(new CodeAttributeDeclaration((string)entry.Key,
							value));
					else
						param.CustomAttributes.Add(new CodeAttributeDeclaration((string)entry.Key));
				}
			}
			attributes.Clear();

			return type;
		}

		public static string ConvertParamName(string input)
		{
			var strRet = input;
			if (s_ParamNames != null)
			{
				foreach (var entry in s_ParamNames)
				{
					if (entry.Regex.IsMatch(input))
						strRet = entry.Regex.Replace(strRet, entry.Replace);
				}
			}

			return strRet.TrimStart(null);
		}

		#endregion

		#region Helper methods

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Handle [in][out] attributes and set direction accordingly
		/// </summary>
		/// <param name="param">parameter declaration</param>
		/// <param name="attributes">list of attributes</param>
		/// ------------------------------------------------------------------------------------
		private void HandleInOut(CodeParameterDeclarationExpression param, IDictionary attributes)
		{
			if (attributes["out"] != null && (bool)attributes["out"])
			{
				if (attributes["in"] != null && (bool)attributes["in"])
				{
					param.Direction = FieldDirection.Ref;
				}
				else
					param.Direction = FieldDirection.Out;
			}
			else
				param.Direction = FieldDirection.In;

			attributes.Remove("out");
			attributes.Remove("in");
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// For a size_is attribute in IDL we have to add a SizeParmIndex to the code which
		/// contains the index of the paramater that specifies the size of the array.
		/// </summary>
		/// <param name="method">The method we're dealing with.</param>
		/// <param name="attributes">list of attributes</param>
		/// ------------------------------------------------------------------------------------
		public void HandleSizeIs(CodeMemberMethod method, IDictionary attributes)
		{
			// loop through all parameters and look if they have a size_is attribute
			foreach(CodeParameterDeclarationExpression param in method.Parameters)
			{
				if (!param.UserData.Contains("size_is"))
					continue;

				var attributeParamName = "SizeParamIndex";
				var varName = ConvertParamName((string)param.UserData["size_is"]);
				if (int.TryParse(varName, out var nValue))
				{
					// We have a fixed length, so use that
					attributeParamName = "SizeConst";
				}
				else
				{
					// now search for the parameter named varName that contains the size
					// of the array
					int i;
					for (i = 0; i < method.Parameters.Count; i++)
					{
						if (method.Parameters[i].Name == varName)
						{
							nValue = i;
							break;
						}
					}
					if (i == method.Parameters.Count && !attributes.Contains("restricted"))
					{
						// if it's a restricted method we don't care
						Console.WriteLine("Internal error: couldn't find MarshalAs " +
										"attribute for parameter {0} of method {1}", param.Name, method.Name);
						attributes.Add("warning",
							"NOTE: This method probably doesn't work since it caused " +
							$"an error on IDL import for parameter {param.Name}");
					}
				}

				// we found the parameter, now find the attribute
				foreach (CodeAttributeDeclaration attribute in param.CustomAttributes)
				{
					if (attribute.Name == "MarshalAs")
					{
						attribute.Arguments.Add(new CodeAttributeArgument(
							attributeParamName, new CodePrimitiveExpression(nValue)));
					}
				}
			}
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Creates an enum member.
		/// </summary>
		/// <param name="enumName">Name of the enumeration.</param>
		/// <param name="name">The name.</param>
		/// <param name="value">The value.</param>
		/// <returns></returns>
		/// ------------------------------------------------------------------------------------
		public static CodeMemberField CreateEnumMember(string enumName, string name,
			string value)
		{
			var member = new CodeMemberField { Name = name };
			if (s_EnumMemberMapping.ContainsKey(name))
				throw new ApplicationException(
					$"{name} is defined in both {enumName} and {s_EnumMemberMapping[name]}");

			if (enumName != string.Empty)
				s_EnumMemberMapping.Add(name, enumName);

			if (value == string.Empty)
				return member;

			if (int.TryParse(value, out var val) || (value.StartsWith("0x") &&
													int.TryParse(value.Substring(2), NumberStyles.HexNumber, null, out val)))
				member.InitExpression = new CodePrimitiveExpression(val);
			else
			{
				var fieldRef = new CodeFieldReferenceExpression();
				member.InitExpression = fieldRef;

				// The value might be a reference to an enum member defined in another
				// enumeration. While this is fine in C++/IDL, we need to add the enum name
				// to it.
				fieldRef.FieldName = ResolveReferences(enumName, value, fieldRef, false);
			}
			return member;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Resolves references in the value.
		/// </summary>
		/// <param name="enumName">The name of the enumeration.</param>
		/// <param name="value">The value.</param>
		/// <param name="fieldRef">The field ref.</param>
		/// <param name="fFinal"><c>true</c> to process string even if we can't find potential
		/// reference, <c>false</c> to add it to a list for later processing.</param>
		/// <returns></returns>
		/// ------------------------------------------------------------------------------------
		private static string ResolveReferences(string enumName, string value,
			CodeFieldReferenceExpression fieldRef, bool fFinal)
		{
			var bldr = new StringBuilder(value);
			var regex = new Regex(@"\w+");

			var matches = regex.Matches(value);
			for (var i = matches.Count; i > 0; i--)
			{
				var match = matches[i-1];
				var refMember = match.Value;
				if (s_EnumMemberMapping.ContainsKey(refMember))
				{
					// need to do this only if it's defined in a different enumeration
					if (s_EnumMemberMapping[refMember] != enumName)
					{
						bldr.Remove(match.Index, match.Length);
						bldr.Insert(match.Index, $"{s_EnumMemberMapping[refMember]}.{refMember}");
					}
				}
				else if (!fFinal)
				{
					// maybe it's referencing a type that we haven't processed yet, so try it
					// again later
					s_NeedsAdjustment.Add(fieldRef, enumName);
					break;
				}
				// otherwise just leave it as it is
			}
			return bldr.ToString();
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Adjusts the references in enums that we couldn't resolve earlier because they
		/// referenced something that came later in the file.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public static void AdjustReferencesInEnums()
		{
			foreach (var fieldRef in s_NeedsAdjustment.Keys)
			{
				fieldRef.FieldName = ResolveReferences(s_NeedsAdjustment[fieldRef],
					fieldRef.FieldName, fieldRef, true);
			}
		}
		#endregion
	}

}
