using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestArray
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Eval("""
               local arr = {}
               len1 = #arr
               for i = 1, 10 do table.insert(arr, i) end
               first1, last1 = arr[1], arr[10]
               len2 = #arr
               for i = 1, 10 do table.remove(arr, 1) end
               first2, last2 = arr[1], arr[10]
               len3 = #arr
               """);
        
        Assert.Equal(00, L.GetGlobalInteger("len1"));
        Assert.Equal(10, L.GetGlobalInteger("len2"));
        Assert.Equal(00, L.GetGlobalInteger("len3"));
        Assert.Equal(01, L.GetGlobalInteger("first1"));
        Assert.Equal(10, L.GetGlobalInteger("last1"));
        Assert.Equal(00, L.GetGlobalInteger("first2"));
        Assert.Equal(00, L.GetGlobalInteger("last2"));
    }

    [Fact]
    public void Test2()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Eval("""
               local arr = {}
               len1 = #arr
               for i = 1, 10 do table.insert(arr, i) end
               len2 = #arr
               for i = 10, 1, -1 do table.remove(arr, i) end
               first2, last2 = arr[1], arr[10]
               len3 = #arr
               """);
        
        Assert.Equal(00, L.GetGlobalInteger("len1"));
        Assert.Equal(10, L.GetGlobalInteger("len2"));
        Assert.Equal(00, L.GetGlobalInteger("len3"));
        Assert.Equal(00, L.GetGlobalInteger("first2"));
        Assert.Equal(00, L.GetGlobalInteger("last2"));
    }
    
    [Fact]
    public void Test3()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Eval("""
               local arr = {}
               for i = 1, 10 do table.insert(arr, i) end
               table.remove(arr, 5)
               return #arr
               """);
        Assert.Equal(9, L.PopInteger());
    }
    
    [Fact]
    public void Test4()
    {
        // Undefined https://www.lua.org/manual/5.2/manual.html#3.4.6
        // "[...]the length of a table t is only defined if the table is a sequence"
        var L = new LuaState();
        L.OpenLibs();
        L.Eval("""
               local arr = {1,2,3,4,5,6,7,8,9,10}
               arr[5] = nil
               return #arr
               """);
        Assert.Equal(10, L.PopInteger());
    }
    
    [Fact]
    public void Test5()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Eval("""
               local arr = {1,2,3,4,5,6,7,8,9,10}
               arr[5] = nil
               arr[10] = nil
               return #arr
               """);
        Assert.Equal(4, L.PopInteger());
    }

    [Fact]
    public void Test6()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Eval("""
               local arr = {1,2,3,4,5,6,7,8,9,10}
               arr[1] = nil
               arr[5] = nil
               arr[10] = nil
               return table.length(arr)
               """);
        Assert.Equal(7, L.PopInteger());
    }
    
    [Fact]
    public void TestBigArray1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Eval("""
                local t = {};
                for i = 1, 10_000 do
                  table.insert(t, i);
                end
                return t;
               """);

        var t = L.PopTable();
        for (var i = 1; i <= 10_000; i++)
        {
            Assert.True(t.TryGetInt(i, out var val));
            Assert.Equal(i, (int)val.V.NValue);
        }
    }
}