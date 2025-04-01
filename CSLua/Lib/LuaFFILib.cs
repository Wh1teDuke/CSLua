using System.Diagnostics;
using System.Reflection;
using System.Text;
using CSLua.Parse;
using CSLua.Utils;
// ReSharper disable InconsistentNaming

namespace CSLua.Lib;

public static class LuaFFILib
{
	public const string LIB_NAME = "ffi.cs";
	
	public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);

	public static int OpenLib(ILuaState lua)
	{
		Span<NameFuncPair> define =
		[
			new("clear_assembly_list",	FFI_ClearAssemblyList),
			new("add_assembly",			FFI_AddAssembly),

			new("clear_using_list",		FFI_ClearUsingList),
			new("using",				FFI_Using),

			new("parse_signature",		FFI_ParseSignature),

			new("get_type",				FFI_GetType),
			new("get_constructor",		FFI_GetConstructor),
			new("get_static_method",	FFI_GetStaticMethod),
			new("get_method",			FFI_GetMethod),
			new("call_method",			FFI_CallMethod),

			new("get_field",			FFI_GetField),
			new("get_field_value",		FFI_GetFieldValue),
			new("set_field_value",		FFI_SetFieldValue),

			new("get_prop",				FFI_GetProp),
			new("get_static_prop",		FFI_GetStaticProp),
			new("get_prop_value",		FFI_GetPropValue),
			new("set_prop_value",		FFI_SetPropValue),
			// new NameFuncPair("call_constructor", FFI_CallConstructor),
		];

		lua.NewLib(define);
		return 1;
	}

	private static int FFI_ClearAssemblyList(ILuaState lua)
	{
		AssemblyList.Clear();
		return 0;
	}

	private static int FFI_AddAssembly(ILuaState lua)
	{
		var name = lua.ToString(1);
		try
		{
			var assembly = Assembly.Load(name);
			AssemblyList.Add(assembly);
		}
		catch (Exception)
		{
			ULDebug.LogError("Assembly not found:" + name);
		}
		return 0;
	}

	private static int FFI_ClearUsingList(ILuaState lua)
	{
		UsingList.Clear();
		return 0;
	}

	private static int FFI_Using(ILuaState lua)
	{
		var name = lua.ToString(1);
		UsingList.Add(name);
		return 0;
	}

	// Return 'ReturnType', 'FuncName', 'ParameterTypes'
	private static int FFI_ParseSignature(ILuaState lua)
	{
		var signature = lua.ToString(1);
		var result = FuncSignatureParser.Parse(signature);
		if (result.ReturnType != null)
			lua.PushString(result.ReturnType);
		else
			lua.PushNil();
		lua.PushString(result.FuncName);
		if (result.ParameterTypes != null) {
			lua.NewTable();
			for (var i = 0; i < result.ParameterTypes.Length; ++i) 
			{
				lua.PushString(result.ParameterTypes[i]);
				lua.RawSetI(-2, i + 1);
			}
		}
		else 
		{
			lua.PushNil();
		}
		return 3;
	}

	private static int FFI_GetType(ILuaState lua)
	{
		var typename = lua.ToString(1);
		var t = GetType(typename);
		if (t != null)
			lua.PushLightUserData(t);
		else
			lua.PushNil();
		return 1;
	}

	private static int FFI_GetConstructor(ILuaState lua)
	{
		var t = (Type)lua.ToUserData(1);
		var n = lua.RawLen(2);
		var types = new Type[n];
		for (var i = 0; i < n; ++i)
		{
			lua.RawGetI(2, i + 1);
			types[i] = (Type)lua.ToUserData(-1);
			lua.Pop(1);
		}

		var cInfo = t.GetConstructor(types);
		var ffiMethod = new FFIConstructorInfo(cInfo);
		lua.PushLightUserData(ffiMethod);
		return 1;
	}

	private static int GetMethodAux(ILuaState lua, BindingFlags flags)
	{
		var t = (Type)lua.ToUserData(1);
		var mName = lua.ToString(2);
		var n = lua.RawLen(3);
		var types = new Type[n];
		for (var i=0; i < n; ++i)
		{
			lua.RawGetI( 3, i+1 );
			types[i] = (Type)lua.ToUserData(-1);
			lua.Pop(1);
		}
		var minfo = t.GetMethod(mName,
			flags,
			null,
			CallingConventions.Any,
			types,
			null
		);
		if (minfo == null)
			return 0;

		var ffiMethod = new FFIMethodInfo(minfo);
		lua.PushLightUserData(ffiMethod);
		return 1;
	}

	private static int FFI_GetMethod(ILuaState lua)
	{
		return GetMethodAux(lua,
			BindingFlags.Instance |
			BindingFlags.Public |
			BindingFlags.InvokeMethod);
	}

	private static int FFI_GetStaticMethod( ILuaState lua )
	{
		return GetMethodAux( lua,
			BindingFlags.Static |
			BindingFlags.Public |
			BindingFlags.InvokeMethod );
	}

	private static int FFI_CallMethod(ILuaState lua)
	{
		var ffiMethod = (FFIMethodBase)lua.ToUserData(1);
		if (ffiMethod != null)
		{
			try
			{
				return ffiMethod.Call(lua);
			}
			catch( Exception e )
			{
				lua.PushString( "call_method Exception: " + e.Message +
				                "\nSource:\n" + e.Source +
				                "\nStaceTrace:\n" + e.StackTrace );
				lua.Error();
				return 0;
			}
		}

		lua.PushString( "call_method cannot find MethodInfo" );
		lua.Error();
		return 0;
	}

	private static int FFI_GetField(ILuaState lua)
	{
		var t = (Type)lua.ToUserData(1)!;
		var name = lua.ToString(2);
		var fInfo = t.GetField(name,
			BindingFlags.Instance |
			BindingFlags.Public);
		if (fInfo == null)
			throw new LuaException("GetField failed: " + name);
		lua.PushLightUserData(fInfo);
		return 1;
	}

	private static int FFI_GetFieldValue(ILuaState lua)
	{
		var fInfo = (FieldInfo)lua.ToUserData(1)!;
		var inst = lua.ToUserData(2);
		var returnType = (Type)lua.ToUserData(3)!;
		var value = fInfo.GetValue(inst)!;
		LuaStackUtil.PushRawValue(lua, value, returnType);
		return 1;
	}

	private static int FFI_SetFieldValue(ILuaState lua)
	{
		var finfo = (FieldInfo)lua.ToUserData(1)!;
		var inst = lua.ToUserData(2)!;
		var t = (Type)lua.ToUserData(4)!;
		var value = LuaStackUtil.ToRawValue(lua, 3, t);
		finfo.SetValue(inst, value);
		return 0;
	}

	private static int FFI_GetProp(ILuaState lua)
	{
		var t = (Type)lua.ToUserData(1)!;
		var name = lua.ToString(2);
		var pInfo = t.GetProperty(name,
			BindingFlags.Instance |
			BindingFlags.Public);
		if (pInfo == null)
			throw new LuaException("GetProperty failed:" + name);
		lua.PushLightUserData(pInfo);
		return 1;
	}

	private static int FFI_GetStaticProp(ILuaState lua)
	{
		var t = (Type)lua.ToUserData(1)!;
		var name = lua.ToString(2);
		var pinfo = t.GetProperty(name,
			BindingFlags.Static |
			BindingFlags.Public);
		if (pinfo == null)
			throw new LuaException("GetProperty failed:"+name);
		lua.PushLightUserData(pinfo);
		return 1;
	}

	private static int FFI_GetPropValue(ILuaState lua)
	{
		var pinfo = (PropertyInfo)lua.ToUserData(1)!;
		var inst = lua.ToUserData(2);
		var returnType = (Type)lua.ToUserData(3)!;
		var value = pinfo.GetValue(inst, null)!;
		LuaStackUtil.PushRawValue(lua, value, returnType);
		return 1;
	}

	private static int FFI_SetPropValue(ILuaState lua)
	{
		var pinfo = (PropertyInfo)lua.ToUserData(1);
		var inst = lua.ToUserData(2);
		var t = (Type)lua.ToUserData(4);
		var value = LuaStackUtil.ToRawValue(lua, 3, t);
		pinfo.SetValue(inst, value, null);
		return 0;
	}

//////////////////////////////////////////////////////////////////////

	private static readonly List<Assembly> 	AssemblyList = [];
	private static readonly List<string>		UsingList = [];

	private static Type? FindTypeInAllAssemblies(string typename)
	{
		Type? result = null;
		foreach (var t1 in AssemblyList)
		{
			var t = t1.GetType(typename);
			if (t != null)
			{
				result ??= t;
				// TODO: handle error: ambiguous type name
			}
		}
		return result;
	}

	private static Type? GetType(string typename)
	{
		var result = FindTypeInAllAssemblies(typename);
		if (result != null) return result;

		foreach (var t in UsingList)
		{
			var fullname = t + "." + typename;
			result = FindTypeInAllAssemblies(fullname);
			if (result != null) return result;
		}

		return null;
	}

	private static class LuaStackUtil
	{
		public static int PushRawValue(ILuaState lua, object o, Type t)
		{
			switch (t.FullName)
			{
				case "System.Boolean": 
					lua.PushBoolean((bool)o);
					return 1;

				case "System.Char": // TODO: int?
					lua.PushString(((char)o).ToString());
					return 1;

				case "System.Byte": 
					lua.PushNumber((byte)o);
					return 1;

				case "System.SByte": 
					lua.PushNumber((sbyte)o);
					return 1;

				case "System.Int16": 
					lua.PushNumber((short)o);
					return 1;

				case "System.UInt16": 
					lua.PushNumber((ushort)o);
					return 1;

				case "System.Int32": 
					lua.PushNumber((int)o);
					return 1;

				case "System.UInt32": 
					lua.PushNumber((uint)o);
					return 1;

				case "System.Int64": 
					lua.PushInt64((long)o);
					return 1;

				case "System.UInt64": 
					lua.PushInt64((long)o);
					return 1;

				case "System.Single": 
					lua.PushNumber((float)o);
					return 1;

				case "System.Double": 
					lua.PushNumber((double)o);
					return 1;

				case "System.Decimal": 
					lua.PushLightUserData((decimal)o);
					return 1;

				case "System.String": 
					lua.PushString((string)o);
					return 1;

				case "System.Object": 
					lua.PushLightUserData(o);
					return 1;

				default: 
					lua.PushLightUserData(o);
					return 1;
			}
		}

		public static object ToRawValue(ILuaState lua, int index, Type t)
		{
			switch (t.FullName)
			{
				case "System.Boolean":
					return lua.ToBoolean(index);

				case "System.Char": {
					var s = lua.ToString(index);
					if (string.IsNullOrEmpty(s))
						return null;
					return s[0];
				}

				case "System.Byte":
					return (byte)lua.ToNumber( index );

				case "System.SByte":
					return (sbyte)lua.ToNumber( index );

				case "System.Int16":
					return (short)lua.ToNumber( index );

				case "System.UInt16":
					return (ushort)lua.ToNumber( index );

				case "System.Int32":
					return (int)lua.ToNumber( index );

				case "System.UInt32":
					return (uint)lua.ToNumber(index);

				case "System.Int64":
					return (long)lua.ToUserData(index);

				case "System.UInt64":
					return (ulong)lua.ToUserData(index);

				case "System.Single":
					return (float)lua.ToNumber(index);

				case "System.Double":
					return lua.ToNumber(index);

				case "System.Decimal":
					return (decimal)lua.ToUserData(index);

				case "System.String":
					return lua.ToString(index);

				case "System.Object":
					return lua.ToUserData(index);

				default: {
					return lua.ToUserData(index);
				}
			}
		}
	}

	private abstract class FFIMethodBase
	{
		protected FFIMethodBase(MethodBase mInfo)
		{
			_method = mInfo;

			var parameters = mInfo.GetParameters();
			_parameterTypes = new Type[parameters.Length];
			for (var i = 0; i < parameters.Length; ++i) 
				_parameterTypes[i] = parameters[i].ParameterType;
		}

		public int Call(ILuaState lua)
		{
			const int firstParamPos = 3;
			var n = lua.GetTop();
			var inst  = lua.ToUserData(2);
			var nParam = n - firstParamPos + 1;
			var parameters = new object[nParam];
			for (var i = 0; i < nParam; ++i)
			{
				var index = firstParamPos + i;
				var paramType = _parameterTypes[i];
				parameters[i] = LuaStackUtil.ToRawValue(lua, index, paramType);
			}

			var r = _method.Invoke(inst, parameters);
			return PushReturnValue(lua, r);
		}

		private readonly MethodBase 	_method;
		private readonly Type[] 		_parameterTypes;

		protected virtual int PushReturnValue(ILuaState lua, object o) => 0;
	}

	private sealed class FFIMethodInfo(MethodInfo mInfo) : FFIMethodBase(mInfo)
	{
		private readonly Type _returnType = mInfo.ReturnParameter.ParameterType;

		protected override int PushReturnValue(ILuaState lua, object o) =>
			LuaStackUtil.PushRawValue(lua, o, _returnType);
	}

	private sealed class FFIConstructorInfo(
		ConstructorInfo cInfo) : FFIMethodBase(cInfo)
	{
		protected override int PushReturnValue(ILuaState lua, object o)
		{
			lua.PushLightUserData(o);
			return 1;
		}
	}

//////////////////////////////////////////////////////////////////////
	///
	/// SIGNATURE PARSER
	///
//////////////////////////////////////////////////////////////////////
	private sealed class FuncSignature
	{
		public string FuncName;
		public string? ReturnType;
		public string[]? ParameterTypes;
	}

	private readonly record struct FuncSignatureParser(
		LLex Lexer, FuncSignature Result)
	{
		public static FuncSignature Parse(string signature)
		{
			var loadInfo = new StringLoadInfo(signature);
			var parser = new FuncSignatureParser(
				new LLex(loadInfo, signature),
				new FuncSignature()
			);

			return parser.ParseAux(signature);
		}

		private FuncSignature ParseAux(string signature)
		{
			Lexer.Next(); // read first token
			FuncSignature();
			return Result;
		}

		private void FuncSignature()
		{
			var s1 = TypeName();
			var s2 = TypeName();
			if (string.IsNullOrEmpty(s2)) 
			{
				if (string.IsNullOrEmpty(s1)) 
				{
					Lexer.SyntaxError("function name expected");
				}
				else 
				{
					Result.ReturnType = null;
					Result.FuncName = s1;
				}
			}
			else {
				Result.ReturnType = s1;
				Result.FuncName = s2;
			}
			FuncArgs();
			if (Lexer.Token.TokenType != TK.EOS) 
				Lexer.SyntaxError("Redundant tail characters: " + Lexer.Token);
		}

		private string TypeName()
		{
			var sb = new StringBuilder();
			while (Lexer.Token.TokenType == TK.NAME)
			{
				sb.Append(CheckName());

				if (!TestNext('.'))
					break;

				sb.Append('.');
			}
			return sb.ToString();
		}

		private void ReturnType()
		{
			if (Lexer.Token.TokenType == TK.NAME)
			{
				Result.ReturnType = Lexer.Token.Str!;
				Lexer.Next();
			}

			Lexer.SyntaxError("Return type expected");
		}

		private void FuncName()
		{
			if (Lexer.Token.TokenType == TK.NAME)
			{
				if (Lexer.Token.TokenType == TK.NAME)
				{
					Result.ReturnType = Lexer.Token.Str!;
					Lexer.Next();
				}
			}

			Lexer.SyntaxError("Function name expected");
		}

		private string CheckName()
		{
			Debug.Assert(Lexer.Token.Kind == TokenKind.Named);
			var name = Lexer.Token.Str!;
			Lexer.Next();
			return name;
		}

		private void TypeList()
		{
			var typeList = new List<string>();
			while (Lexer.Token.TokenType == TK.NAME) 
			{
				typeList.Add(CheckName());
				if (!TestNext(',')) break;
			}
			Result.ParameterTypes = typeList.ToArray();
		}

		private void FuncArgs()
		{
			if (Lexer.Token.Val1 != '(') return;

			var line = Lexer.LineNumber;
			Lexer.Next();
			if (TestNext( ')')) 
			{
				Result.ParameterTypes = [];
				return;
			}

			TypeList();
			CheckMatch(')', '(', line);
		}

		private bool TestNext(char tokenType)
		{
			if (Lexer.Token.Val1 != tokenType) return false;
			Lexer.Next();
			return true;
		}

		private void ErrorExpected(char token) => 
			Lexer.SyntaxError($"{token} expected");

		private void CheckMatch(char what, int who, int where)
		{
			if (TestNext(what)) return;
			if (where == Lexer.LineNumber)
				ErrorExpected(what);
			else
				Lexer.SyntaxError(
					$"{((char)what).ToString()} expected (to close {((char)who).ToString()} at line {where})");
		}
	}
}