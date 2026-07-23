using BenchmarkDotNet.Attributes;
using CSLua;
using CSLua.Extensions;

namespace Benchmark;

[MemoryDiagnoser]
public class CSharp_Sum
{
    private LuaState _luaState = null!;
    
    [Params(5_000, 50_000)]
    public int LoopCount { get; set; }
    
    [GlobalSetup]
    public void Setup()
    {
        _luaState = new LuaState();
        _luaState.DoString("""
                           function AddLoop(count)
                               local sum = 0
                               for i = 1, count do
                                   sum = sum + i
                               end
                               return sum
                           end
                           """);
    }
    
    [Benchmark(Baseline = true)]
    public int Csharp()
    {
        var sum = 0;
        for (var i = 1; i <= LoopCount; i++) sum += i;
        return sum;
    }
    
    [Benchmark]
    public double CsLua()
    {
        _luaState.GetGlobal("AddLoop");
        _luaState.PushNumber(LoopCount);
        _luaState.Call(1, 1);
        
        return _luaState.Status != ThreadStatus.LUA_OK 
            ? throw new Exception(_luaState.PopErrorMsg()) 
            : _luaState.PopNumber();
    }
}