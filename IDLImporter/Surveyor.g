header {
// Copyright (c) 2007-2015 SIL International
// This software is licensed under the LGPL, version 2.1 or later
// (http://www.gnu.org/licenses/lgpl-2.1.html)

// Defines the grammar for processing some Surveyor tags.

#pragma warning disable 0618,0219, 0162

using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Text;
}

options {
	language = "CSharp";
	namespace  =  "SIL.IdlImporterTool";
}

//-----------------------------------------------------------------------------------------------
// Surveyor Parser rules
//-----------------------------------------------------------------------------------------------
class SurveyorParser extends Parser;
options {
	buildAST = true;	// uses CommonAST by default
	k = 2;
}

{
		private StringBuilder m_bldr;

		public SurveyorParser(StringBuilder bldr, TokenStream lexer) : this(lexer,2)
		{
			m_bldr = bldr;
		}
}


surveyorTags
	:	(table
	|	(DOLLAR LBRACE) => reference
	|	o:other
		{ m_bldr.Append(#o.getText()); }
	|	http
	)*
	;

other
	:	IDENTIFIER
	|	DOLLAR
	;

table
	:	TABLE { m_bldr.Append("<list type=\"table\">"); }
		(r:row { m_bldr.Append(#r.getText()); })+
		RBRACE { m_bldr.Append("</list>"); }
	;

row
	:	ROW f:CELL s:CELL RBRACE
		{
			currentAST.root.setText(string.Format("<item><term>{0}</term>{2} <description>{2} {1}{2} </description>{2} </item>",
				#f.getText(), #s.getText(), Environment.NewLine));
		}
	;

http
	:	h:HTTP
		{ m_bldr.Append(string.Format("<see href=\"{0}\">{0}</see>", #h.getText())); }
	;

reference
	:	r:REFERENCE
		{ m_bldr.Append(string.Format("<c>{0}</c>", #r.getText().Replace("#", "."))); }
	;

//-----------------------------------------------------------------------------------------------
//	Surveyor Lexical rules
//-----------------------------------------------------------------------------------------------
class SurveyorLexer extends Lexer;
options {
	k=2; // needed for comments
	charVocabulary='\3'..'\377';
	caseSensitiveLiterals=true;
	testLiterals=true;
	filter=IGNORE;
}

{
		private StringBuilder m_bldr;

		public SurveyorLexer(StringBuilder bldr, TextReader r) : this(r)
		{
			m_bldr = bldr;
		}
}

TABLE
	:	"@table{"
	;

ROW
	:	"@row{"
	;

CELL
	:	"@cell{"! (~'}')* RBRACE!
	;

HTTP
	:	"@HTTP{"! (~'}')* RBRACE!
	;

REFERENCE
	:	DOLLAR! LBRACE!
		(IDENTIFIER (POUND IDENTIFIER)?
		| POUND! IDENTIFIER
		)
		RBRACE!
	;

LBRACE
	:	'{'
	;

RBRACE
	:	'}'
	;

DOLLAR
	:	'$'
	;

POUND
	:	'#'
	;

protected
IDENTIFIER
	:	LETTER (LETTER | DIGIT)*
	;

protected
DIGIT
	:	'0'..'9'
	;

protected
LETTER
	: 'A'..'Z'
	| 'a'..'z'
	| '_'
	;

protected
WS
	:	'\n' { newline(); }
	|	'\r' '\n' { newline(); }
	|	' '
	|	'\t'
	{$setType(Token.SKIP);}
	;

protected
IGNORE
	:	('\n') => '\n' { newline(); m_bldr.AppendLine(); }
	|	('\r') => '\r'
	|	c:. { m_bldr.Append(c);}
  ;