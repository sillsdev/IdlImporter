// // Copyright (c) 2020 SIL International
// // This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;

namespace SIL.IdlImporterTool
{
	public class ConsoleLogger: ILog
	{
		public void Error(string   text)
		{
			Console.Error.WriteLine($"ERROR: {text}");
		}

		public void Warning(string text)
		{
			Console.Error.WriteLine($"WARNING: {text}");
		}

		public void Message(string text)
		{
			Console.WriteLine(text);
		}
	}
}