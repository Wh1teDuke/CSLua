using CSLua.Util;

namespace CSLua.Parse;

public sealed class LuaParserException(string msg): 
    LuaRuntimeException(ThreadStatus.LUA_ERRSYNTAX, msg);