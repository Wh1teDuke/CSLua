using BenchmarkDotNet.Attributes;
using CSLua;
using CSLua.Extensions;

namespace Benchmark;

[MemoryDiagnoser]
public class CSharp_String
{
    private LuaState _luaState = null!;
    
    [Params(100, 1_000)]
    public int LoopCount { get; set; }
    
    [GlobalSetup]
    public void Setup()
    {
        _luaState = Lua.New();
        _luaState.DoString("""
                           function AddLoop(count)
                               local str = ""
                               for i = 0, count - 1 do
                                   str = str .. i
                               end
                               return str
                           end
                           """);
    }
    
    [Benchmark(Baseline = true)]
    public string Csharp()
    {
        var str = "";
        for (var i = 0; i < LoopCount; i++) str += i;
        return str;
    }
    
    [Benchmark]
    public string CsLua()
    {
        _luaState.GetGlobal("AddLoop");
        _luaState.PushNumber(LoopCount);
        _luaState.Call(1, 1);
        
        return _luaState.Status != ThreadStatus.LUA_OK 
            ? throw new Exception(_luaState.PopErrorMsg()) 
            : _luaState.PopString()!;
    }
}