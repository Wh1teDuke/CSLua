using CSLua.Util;

namespace CSLua.Lib;

public static class LuaConsoleLib
{
    public const string LIB_NAME = "console";
	
    public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);

    public static int OpenLib(LuaState lua)
    {
        ReadOnlySpan<NameFuncPair> define =
        [
            new("write",        Console_Write),
            new("writeline",    Console_WriteLine),
            new("clear",        Console_Clear),
            new("readkeychar",  Console_GetKey),
            new("beep",         Console_Beep),
        ];

        lua.NewLib(define);

        foreach (var color in Enum.GetValues<ConsoleColor>()) 
            SetField(lua, "color_" + color.ToString().ToLower(), (int)color);

        var mt = new LuaTable(lua);

        // Getter
        mt.Set("__index", L =>
        {
            var table = L.CheckTable(1);
            var key = L.CheckString(2);

            return key switch
            {
                "backgroundcolor" or "bgcolor" => Console_GetBackgroundColor(L),
                "foregroundcolor" or "fgcolor" => Console_GetForegroundColor(L),
                "cursorleft" => Console_CursorLeft(L),
                "cursortop" => Console_CursorTop(L),
                "keyavailable" => Console_KeyAvailable(L),
                _ => 0
            };
        });
        
        // Setter
        mt.Set("__newindex", L =>
        {
            var table = L.CheckTable(1);
            var key = L.CheckString(2);

            switch (key)
            {
                case "backgroundcolor" or "bgcolor":
                    var bgcolor = (ConsoleColor)lua.CheckInteger(3);
                    Console.BackgroundColor = bgcolor;
                    break;
                case "foregroundcolor" or "fgcolor":
                    var fgcolor = (ConsoleColor)lua.CheckInteger(3);
                    Console.ForegroundColor = fgcolor;
                    break;
                case "cursorleft":
                    Console.CursorLeft = lua.CheckInteger(3);
                    break;
                case "cursortop":
                    Console.CursorTop = lua.CheckInteger(3);
                    break;
                case "title":
                    Console.Title = lua.CheckString(3);
                    break;
                default:
                    return 0;
            }
            
            return 0;
        });
        
        lua.PushTable(mt);
        lua.SetMetaTable(-2);
        
        return 1;
    }
    
    private static void SetField(LuaState L, string key, long value)
    {
        L.PushInt64(value);
        L.SetField(-2, key);
    }
    
    private static int Console_Write(LuaState lua)
    {
        var sb = LuaUtil.StrBuilder;
        var n = lua.GetTop();
        LuaBaseLib.FillStringBuilder(lua, sb, n);
        foreach (var chunk in sb.GetChunks())
            Console.Write(chunk.Span);
        sb.Clear();

        return 0;
    }
    
    private static int Console_WriteLine(LuaState lua)
    {
        var sb = LuaUtil.StrBuilder;
        var n = lua.GetTop();
        LuaBaseLib.FillStringBuilder(lua, sb, n);
        foreach (var chunk in sb.GetChunks())
            Console.WriteLine(chunk.Span);
        sb.Clear();

        return 0;
    }
    
    public static int Console_GetBackgroundColor(LuaState lua)
    {
        var color = (int)Console.BackgroundColor;
        lua.PushInt64(color);
        return 1;
    }
    
    public static int Console_GetForegroundColor(LuaState lua)
    {
        var color = (int)Console.ForegroundColor;
        lua.PushInt64(color);
        return 1;
    }
    
    public static int Console_Clear(LuaState lua)
    {
        Console.Clear();
        return 0;
    }
    
    public static int Console_CursorLeft(LuaState lua)
    {
        lua.PushInteger(Console.CursorLeft);
        return 1;
    }
    
    public static int Console_CursorTop(LuaState lua)
    {
        lua.PushInteger(Console.CursorTop);
        return 1;
    }
    
    public static int Console_KeyAvailable(LuaState lua)
    {
        lua.PushBoolean(Console.KeyAvailable);
        return 1;
    }
    
    public static int Console_GetKey(LuaState lua)
    {
        var intercept = lua.OptBoolean(1, false);
        lua.PushString(Console.ReadKey(intercept).KeyChar+"");
        return 1;
    }

    public static int Console_Beep(LuaState lua)
    {
        /*if (lua.IsNumber(1) && lua.IsNumber(2))
            Console.Beep(lua.CheckInteger(1), lua.CheckInteger(2));
        else*/
        Console.Beep();
        return 0;
    }
}