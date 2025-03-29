using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestList
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        L.L_OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);

        L.Eval("""
               local list = require 'list'
               local list1 = list.new(1, 2, 3, 4, 5)

               assert(list.contains(list1, 1))
               assert(list.indexof(list1, 1) == 0)
               assert(list.len(list1) == 5)

               list.del(list1, 0)

               assert(list.len(list1) == 4)
               assert(not list.contains(list1, 1))
               assert(list.indexof(list1, 1) == -1)

               return list.get(list1, 3)
               """);

        var i = L.PopInteger();
        Assert.Equal(5, i);
    }

    [Fact]
    public void Test2()
    {
        var L = new LuaState();
        L.L_OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);

        L.Eval("""
                local list = require 'list'
                local list1 = list.new()
                assert(list.isempty(list1))
                list.add(list1, 1, 2, 3, 4, 5)
                assert(not list.isempty(list1))
                list.del(list1, 2, true)
                return list.get(list1, 2)
               """);
        var i = L.PopInteger();
        Assert.Equal(5, i);
    }

    [Fact]
    public void TestNext1()
    {
        var L = new LuaState();
        L.L_OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);
        
        L.Eval("""
               local list = require 'list'
               local list1 = list.new(1, 2, 3)

               local i, v = list.next(list1)
               local res = 0

               while i do
                 res += v
                 i, v = list.next(list1, i)
               end

               return res
               """);
        var i = L.PopInteger();
        Assert.Equal(6, i);
    }
}