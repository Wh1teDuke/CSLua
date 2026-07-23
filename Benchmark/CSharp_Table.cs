using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CSLua;
using CSLua.Extensions;

namespace Benchmark;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CSharp_Table
{
    private LuaState _luaState = null!;
    private Dictionary<string, string> _dict = null!;
    
    [GlobalSetup]
    public void Setup()
    {
        _dict           = [];
        _dict["Foo"]    = "Bar";
        _dict["Foo1"]   = "Bar";
        _dict["Foo2"]   = "Bar";
        _dict["Foo3"]   = "Bar";

        _luaState = Lua.New();
        _luaState.OpenLibs();
        
        _luaState.DoString(
            "dict = {Foo='Bar',Foo1='Bar',Foo2='Bar',Foo3='Bar'}");

        _luaState.Compile(
            "_dict_get", """
                     local a = dict.Foo
                     local b = dict.Foo1
                     local c = dict.Foo2
                     local d = dict.Foo3 
                     return 4
                     """);

        _luaState.Compile(
            "_dict_set", """
                     dict.Bar  = "Foo"
                     dict.Bar1 = "Foo1"
                     dict.Bar2 = "Foo2"
                     dict.Bar3 = "Foo3"
                     return 4
                     """);
        
        _luaState.Compile(
            "_dict_iter", """
                         for k, v in pairs(dict) do
                         end
                         return 4
                         """);
        
        _luaState.Compile(
            "_dict_del", """
                          dict["nil1"] = nil
                          dict["nil2"] = nil
                          dict["nil3"] = nil
                          dict["nil4"] = nil
                          return 4
                          """);
    }
    
    #region Get
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Get")]
    public int CsharpGet()
    {
        var a = _dict["Foo"];
        var b = _dict["Foo1"];
        var c = _dict["Foo2"];
        var d = _dict["Foo3"];
        return a.Length + b.Length + c.Length + d.Length; // Don't optimize lookup
    }

    [Benchmark]
    [BenchmarkCategory("Get")]
    public int CsLuaGet()
    {
        _luaState.GetGlobal("_dict_get");
        _luaState.Call(0, 1);
        return _luaState.PopInteger();
    }
    #endregion
    
    #region Set
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Set")]
    public int CsharpSet()
    {
        _dict["Bar"]  = "Foo";
        _dict["Bar1"] = "Foo1";
        _dict["Bar2"] = "Foo2";
        _dict["Bar3"] = "Foo3";
        return _dict.Count;
    }

    [Benchmark]
    [BenchmarkCategory("Set")]
    public int CsLuaSet()
    {
        _luaState.GetGlobal("_dict_set");
        _luaState.Call(0, 1);
        return _luaState.PopInteger();
    }
    #endregion
    
    #region Iter
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Iter")]
    public int CsharpIter()
    {
        var res = 0;
        foreach(var (key, value) in _dict) res++;
        return res;
    }

    [Benchmark]
    [BenchmarkCategory("Iter")]
    public int CsLuaIter()
    {
        _luaState.GetGlobal("_dict_iter");
        _luaState.Call(0, 1);
        return _luaState.PopInteger();
    }
    #endregion
    
    #region Del
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Del")]
    public int CsharpDel()
    {
        _dict.Remove("nil1");
        _dict.Remove("nil2");
        _dict.Remove("nil3");
        _dict.Remove("nil4");
        return _dict.Count;
    }

    [Benchmark]
    [BenchmarkCategory("Del")]
    public int CsLuaDel()
    {
        _luaState.GetGlobal("_dict_del");
        _luaState.Call(0, 1);
        return _luaState.PopInteger();
    }
    #endregion
}