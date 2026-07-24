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
        _dict["Foo4"]   = "Bar";
        _dict["Foo5"]   = "Bar";
        _dict["Foo6"]   = "Bar";
        _dict["Foo7"]   = "Bar";
        _dict["Foo8"]   = "Bar";

        _luaState = Lua.New();
        _luaState.OpenLibs();
        
        _luaState.DoString(
            "dict = {Foo='Bar',Foo1='Bar',Foo2='Bar',Foo3='Bar',Foo4='Bar',Foo5='Bar',Foo6='Bar',Foo7='Bar',Foo8='Bar'}");

        _luaState.Compile(
            "_dict_get", """
                     local a = dict.Foo
                     local b = dict.Foo1
                     local c = dict.Foo2
                     local d = dict.Foo3
                     local e = dict.Foo4
                     local f = dict.Foo5
                     local g = dict.Foo6
                     local h = dict.Foo7
                     local i = dict.Foo8
                     return 4
                     """);

        _luaState.Compile(
            "_dict_set", """
                     dict.Bar  = "Foo"
                     dict.Bar1 = "Foo1"
                     dict.Bar2 = "Foo2"
                     dict.Bar3 = "Foo3"
                     dict.Bar4 = "Foo4"
                     dict.Bar5 = "Foo5"
                     dict.Bar6 = "Foo6"
                     dict.Bar7 = "Foo7"
                     dict.Bar8 = "Foo8"
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
                          dict["nil5"] = nil
                          dict["nil6"] = nil
                          dict["nil7"] = nil
                          dict["nil8"] = nil
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
        var e = _dict["Foo4"];
        var f = _dict["Foo5"];
        var g = _dict["Foo6"];
        var h = _dict["Foo7"];
        var i = _dict["Foo8"];
        return a.Length + b.Length + c.Length + d.Length + e.Length + 
               f.Length + g.Length + h.Length + i.Length; // Don't optimize lookup
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
        _dict["Bar4"] = "Foo4";
        _dict["Bar5"] = "Foo5";
        _dict["Bar6"] = "Foo6";
        _dict["Bar7"] = "Foo7";
        _dict["Bar8"] = "Foo8";
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
        _dict.Remove("nil5");
        _dict.Remove("nil6");
        _dict.Remove("nil7");
        _dict.Remove("nil8");
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