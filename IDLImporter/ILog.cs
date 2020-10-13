// Copyright (c) 2020 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

namespace SIL.IdlImporterTool
{
	public interface ILog
	{
		void Error(string text);
		void Warning(string text);
		void Message(string text);
	}
}