using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using CSLua.Util;
using NumberStyles = System.Globalization.NumberStyles;
// ReSharper disable InconsistentNaming

namespace CSLua.Parse;

public enum TK
{
	// reserved words
	AND = 257,
	BREAK,
	CONTINUE,
	DO,
	ELSE,
	ELSEIF,
	END,
	FALSE,
	FOR,
	FUNCTION,
	GOTO,
	IF,
	IN,
	LOCAL,
	NIL,
	NOT,
	OR,
	REPEAT,
	RETURN,
	THEN,
	TRUE,
	UNTIL,
	WHILE,
	// other terminal symbols
	CONCAT,
	DOTS,
	EQ,
	GE,
	LE,
	NE,
	DBCOLON,
	NUMBER,
	STRING,
	NAME,
	EOS,
	// Compound OP START
	COMP_START,
	PLUSEQ,
	SUBEQ,
	MULTEQ,
	DIVEQ,
	MODEQ,
	BANDEQ,
	BOREQ,
	CONCATEQ,
	COMP_END,
	// Compound OP END
}

public enum TokenKind : byte
{ Typed, Literal, String, LongString, Named, Number }

public readonly record struct LuaToken(
	TokenKind Kind, int Val1, double Val2, string? str = null)
{
	public TK TokenType => (TK)Val1;
	public string? Str => str;
	
	public static LuaToken Literal(int val) => new(TokenKind.Literal, val, 0);
	public static LuaToken String(string str) => new(TokenKind.String, (int)TK.STRING, 0, str);
	public static LuaToken LongString(string str) => new(TokenKind.LongString, (int)TK.STRING, 0, str);
	public static LuaToken Named(string str) => new(TokenKind.Named, (int)TK.NAME, 0, str);
	public static LuaToken Number(double val) => new(TokenKind.Number, (int)TK.NUMBER, val);
	public static LuaToken Of(TK kind) => new(TokenKind.Typed, (int)kind, 0);

	public override string ToString()
	{
		return Kind switch
		{
			TokenKind.Typed => $"TypedToken: {TokenType}",
			TokenKind.Literal => $"TokenLiteral: {Val1}",
			TokenKind.String => $"TokenString: {Str}",
			TokenKind.LongString => $"TokenLongString: {Str}",
			TokenKind.Named => $"TokenNamed: {Str}",
			TokenKind.Number => $"TokenNumber: {Val2}",
			_ => throw new ArgumentOutOfRangeException()
		};
	}
}

public sealed class LLex
{
	private const char EOZ = char.MaxValue;

	public LuaToken Token;
	public int LineNumber;
	public int ColumnNumber;
	public int LastLine;

	private readonly string? _name;
	private readonly ILoadInfo _loadInfo;
	private LuaToken? _lookAhead;
	private int _current;

	private char[] _saved = new char[64];
	private int _savedCount;
	private int _savedStart;

	private static readonly FrozenDictionary<string, TK> ReservedWordDict =
		new Dictionary<string, TK>
		{
		{ "local", TK.LOCAL },
		{ "and", TK.AND },
		{ "do", TK.DO },
		{ "else", TK.ELSE },
		{ "continue", TK.CONTINUE },
		{ "elseif", TK.ELSEIF },
		{ "end", TK.END },
		{ "false", TK.FALSE },
		{ "for", TK.FOR },
		{ "function", TK.FUNCTION },
		{ "break", TK.BREAK },
		{ "if", TK.IF },
		{ "in", TK.IN },
		{ "nil", TK.NIL },
		{ "not", TK.NOT },
		{ "or", TK.OR },
		{ "repeat", TK.REPEAT },
		{ "return", TK.RETURN },
		{ "then", TK.THEN },
		{ "true", TK.TRUE },
		{ "until", TK.UNTIL },
		{ "while", TK.WHILE },
		{ "goto", TK.GOTO },
	}.ToFrozenDictionary(StringComparer.Ordinal);

	private static readonly FrozenDictionary<string, TK>
		.AlternateLookup<ReadOnlySpan<char>> AltRWD = 
			ReservedWordDict.GetAlternateLookup<ReadOnlySpan<char>>(); 

	public LLex(ILoadInfo loadInfo, string? name)
	{
		_loadInfo = loadInfo;
		_name = name;
		LineNumber = 1;
		LastLine = 1;
		Token = LuaToken.Literal('.');
		_lookAhead = null;

		_Next();
	}

	public string Source()
	{
		if (_loadInfo is StringLoadInfo sli) return sli.Source;
		return _name ?? "???";
	}

	public void Next()
	{
		LastLine = LineNumber;
		if (_lookAhead is {} lookAhead)
		{
			Token = lookAhead;
			_lookAhead = null;
		}
		else
		{
			Token = _Lex();
		}
	}

	public LuaToken GetLookAhead()
	{
		LuaUtil.Assert(_lookAhead == null);
		_lookAhead = _Lex(false);
		return _lookAhead.Value;
	}

	private string GetTokenString()
	{
		var result = _GetSavedString();
		ClearSaved();
		return result;
	}

	private void _Next()
	{
		var c = _loadInfo.ReadByte();
		_current = (c == -1) ? EOZ : c;
		ColumnNumber++;
	}

	private void SaveAndNext()
	{
		Save((char)_current);
		_Next();
	}

	private void Save(char c)
	{
		if (_savedCount >= _saved.Length)
			Array.Resize(ref _saved, _saved.Length * 2);
		_saved[_savedCount++] = c;
	}

	private string _GetSavedString() => _GetSavedSpan().ToString();
	
	private ReadOnlySpan<char> _GetSavedSpan() => new(_saved, _savedStart, _savedCount);

	private void ClearSaved()
	{
		_savedCount = 0;
		_savedStart = 0;
	}

	private bool CurrentIsNewLine() => _current is '\n' or '\r';

	private bool CurrentIsDigit() => char.IsDigit((char)_current);

	private bool CurrentIsXDigit()
	{
		return CurrentIsDigit() ||
	       _current is >= 'A' and <= 'F' ||
	       _current is >= 'a' and <= 'f';
	}

	private bool _CurrentIsSpace() => char.IsWhiteSpace((char)_current);

	private bool _CurrentIsAlpha() => char.IsLetter((char)_current);

	private static bool IsReserved(
		ReadOnlySpan<char> identifier, out TK type)
	{
		if (AltRWD.TryGetValue(identifier, out type)) return true;
		type = TK.NAME;
		return false;
	}

	public static bool IsReservedWord(string name) => 
		ReservedWordDict.ContainsKey(name);

	private void _IncLineNumber()
	{
		var old = _current;
		ColumnNumber = 0;
		_Next();
		if (CurrentIsNewLine() && _current != old)
			_Next();
		if (++LineNumber >= int.MaxValue)
			_Error("Chunk has too many lines");
	}

	private ReadOnlySpan<char> _ReadLongString(int sep)
	{
		SaveAndNext();

		if (CurrentIsNewLine())
			_IncLineNumber();

		while (true)
		{
			switch (_current)
			{
				case EOZ:
					_LexError(_GetSavedString(),
						"Unfinished long string/comment",
						TK.EOS);
					break;

				case '[':
				{
					if (_SkipSep() == sep)
					{
						SaveAndNext();
						if (sep == 0)
						{
							_LexError( _GetSavedString(),
								"Nesting of [[...]] is deprecated",
								TK.EOS);
						}
					}
					break;
				}

				case ']':
				{
					if (_SkipSep() == sep)
					{
						SaveAndNext();
						goto endloop;
					}
					break;
				}

				case '\n':
				case '\r':
				{
					Save('\n');
					_IncLineNumber();
					break;
				}

				default:
				{
					SaveAndNext();
					break;
				}
			}
		}
		endloop:
		_savedStart = 2 + sep;
		_savedCount -= 2 + _savedStart;
		return _GetSavedSpan();
	}

	private void _EscapeError(string info, string msg) => 
		_LexError("\\" + info, msg, TK.STRING);

	private byte _ReadHexEscape()
	{
		var r = 0;
		Span<char> c = ['x', (char)0, (char)0];
		// read two hex digits
		for (var i = 1; i < 3; ++i)
		{
			_Next();
			c[i] = (char)_current;
			if (!CurrentIsXDigit())
			{
				_EscapeError(new(c.ToArray(), 0, i + 1),
					"Hexadecimal digit expected");
				// error
			}

			r = (r << 4) + int.Parse(
				[(char)_current], NumberStyles.HexNumber);
		}
		return (byte)r;
	}

	private byte _ReadDecEscape()
	{
		var r = 0;
		Span<char> c = stackalloc char[3];
		// read up to 3 digits
		int i;
		for (i = 0; i < 3 && CurrentIsDigit(); ++i)
		{
			c[i] = (char)_current;
			r = r * 10 + _current - '0';
			_Next();
		}
		if (r > byte.MaxValue)
			_EscapeError(new(c.ToArray(), 0, i),
				"Decimal escape too large");
		return (byte)r;
	}

	private void _ReadString()
	{
		var del = _current;
		_Next();
		while (_current != del)
		{
			switch (_current)
			{
				case EOZ:
					_Error("Unfinished string");
					continue;

				case '\n':
				case '\r':
					_Error("Unfinished string");
					continue;

				case '\\':
				{
					byte c;
					_Next();
					switch (_current)
					{
						case 'a': c=(byte)'\a'; break;
						case 'b': c=(byte)'\b'; break;
						case 'f': c=(byte)'\f'; break;
						case 'n': c=(byte)'\n'; break;
						case 'r': c=(byte)'\r'; break;
						case 't': c=(byte)'\t'; break;
						case 'v': c=(byte)'\v'; break;
						case 'x': c=_ReadHexEscape(); break;

						case '\n':
						case '\r': Save('\n'); _IncLineNumber(); continue;

						case '\\':
						case '\"':
						case '\'': c=(byte)_current; break;

						case EOZ: continue;

						// zap following span of spaces
						case 'z': 
							_Next(); // skip `z'
							while (_CurrentIsSpace())
							{
								if (CurrentIsNewLine())
									_IncLineNumber();
								else
									_Next();
							}
							continue;

						default:
						{
							if (!CurrentIsDigit())
								_EscapeError( _current.ToString(),
									"Invalid escape sequence");

							// digital escape \ddd
							c = _ReadDecEscape();
							Save((char)c);
							continue;
						}
					}
					Save((char)c);
					_Next();
					continue;
				}

				default:
					SaveAndNext();
					continue;
			}
		}
		_Next();
	}

	private double _ReadNumber()
	{
		Span<char> expo = ['E', 'e'];
		Span<char> expoB = ['P', 'p'];
		LuaUtil.Assert(CurrentIsDigit());
		var first = _current;
		SaveAndNext();
		if (first == '0' && _current is 'X' or 'x')
		{
			expo = expoB;
			SaveAndNext();
		}

		for (;;)
		{
			if (_current == expo[0] || _current == expo[1])
			{
				SaveAndNext();
				if (_current is '+' or '-')
					SaveAndNext();
			}
			if (CurrentIsXDigit() || _current == '.')
				SaveAndNext();
			else if (_current == '_')
				_Next();
			else
				break;
		}

		var str = _GetSavedSpan();
		if (LuaUtil.Str2Decimal(str, out var ret))
			return ret;

		_Error("Malformed number: " + _GetSavedString());
		return 0.0;
	}

	[DoesNotReturn]
	private void _Error(string error)
	{
		var src = _name;
		if (_loadInfo is StringLoadInfo sli && src == null)
		{
			src = "[string \"" + sli.Source;
			if (src.Length > LuaDef.LUA_IDSIZE)
			{
				src = src[..(LuaDef.LUA_IDSIZE - 14)];
				src += "...";
			}
			src = Regex.Replace(
				src.Replace('\n', ' ') + "\"]", @"\s+", " ");
		}
		
		var msg = $"{src}:{LineNumber}: {error}";
		throw new LuaParserException(msg);
	}

	[DoesNotReturn]
	private void _LexError(string info, string msg, TK tokenType) =>
		_Error(msg + ":" + (String: info, Token: tokenType));

	[DoesNotReturn]
	public void SyntaxError(string msg)
	{
		// TODO
		_Error(msg);
		//_Error(msg);
	}

	private int _SkipSep()
	{
		var count = 0;
		var boundary = _current;
		SaveAndNext();
		while (_current == '=') 
		{
			SaveAndNext();
			count++;
		}
		return (_current == boundary ? count : (-count) - 1);
	}

	private LuaToken _Lex(bool clearSaved = true)
	{
		if (clearSaved)
			ClearSaved();
		while (true)
		{
			var t = _current;
			switch (t)
			{
				case '\n':
				case '\r': {
					_IncLineNumber();
					continue;
				}

				case '-': {
					_Next();
					if (_current != '-')
					{
						if (_current == '=')
						{
							_Next();
							return LuaToken.Of(TK.SUBEQ);
						}
						return LuaToken.Literal(t);
					}

					// else is a long comment
					_Next();
					if (_current == '[')
					{
						var sep = _SkipSep();
						ClearSaved();
						if (sep >= 0)
						{
							_ReadLongString(sep);
							ClearSaved();
							continue;
						}
					}

					// else is a short comment
					while (!CurrentIsNewLine() && _current != EOZ)
						_Next();
					continue;
				}
				
				case '+': 
					_Next();
					if (_current != '=') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.PLUSEQ);
				
				case '*': 
					_Next();
					if (_current != '=') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.MULTEQ);
				
				case '/': 
					_Next();
					if (_current != '=') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.DIVEQ);
				
				case '%': 
					_Next();
					if (_current != '=') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.MODEQ);
				
				case '&': 
					_Next();
					if (_current != '=') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.BANDEQ);
				
				case '|': 
					_Next();
					if (_current != '=') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.BOREQ);

				case '[': {
					var sep = _SkipSep();
					if (sep >= 0) {
						var semInfo = _ReadLongString(sep);
						var str = GetTokenString();
						return LuaToken.LongString(str);
					}

					if (sep == -1) return LuaToken.Literal(t);
					_Error("invalid long string delimiter");
					continue;
				}

				case '=': 
					_Next();
					if (_current != '=') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.EQ);

				case '<': 
					_Next();
					if (_current != '=') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.LE);

				case '>': 
					_Next();
					if (_current != '=') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.GE);

				case '!':
				case '~': 
					_Next();
					if (_current != '=') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.NE);

				case ':': 
					_Next();
					if (_current != ':') return LuaToken.Literal(t);
					_Next();
					return LuaToken.Of(TK.DBCOLON); // new in 5.2 ?

				case '"':
				case '\'': 
					_ReadString();
					return LuaToken.String(GetTokenString());

				case '.': {
					SaveAndNext();
					if (_current == '.')
					{
						SaveAndNext();
						if (_current == '.')
						{
							SaveAndNext();
							return LuaToken.Of(TK.DOTS);
						}
						else if (_current == '=')
						{
							SaveAndNext();
							return LuaToken.Of(TK.CONCATEQ);
						}

						return LuaToken.Of(TK.CONCAT);
					}

					if (!CurrentIsDigit())
						return LuaToken.Literal(t);
					return LuaToken.Number(_ReadNumber());
				}

				case EOZ: 
					return LuaToken.Of(TK.EOS);

				default:
				{
					if (_CurrentIsSpace())
					{
						_Next();
						continue;
					}

					if (CurrentIsDigit())
						return LuaToken.Number(_ReadNumber());

					if (_CurrentIsAlpha() || _current == '_')
					{
						do { SaveAndNext(); } 
						while (_CurrentIsAlpha() || CurrentIsDigit() ||
						    _current == '_');

						LuaUtil.Assert(_savedCount > 0);
						var identifier = _GetSavedSpan();
						return IsReserved(identifier, out var type) 
							? LuaToken.Of(type) 
							: LuaToken.Named(GetTokenString());
					}

					var c = _current;
					_Next();

					return LuaToken.Literal(c);
				}
			}
		}
	}
}