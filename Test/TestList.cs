using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestList
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);

        L.Eval("""
               local list = require 'list'
               local list1 = list.new(1, 2, 3, 4, 5)

               assert(list.contains(list1, 1))
               assert(list.indexof(list1, 1) == 0)
               assert(list.length(list1) == 5)

               list.remove(list1, 0)

               assert(list.length(list1) == 4)
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
        L.OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);

        L.Eval("""
                local list = require 'list'
                local list1 = list.new()
                assert(list.isempty(list1))
                list.add(list1, 1, 2, 3, 4, 5)
                assert(not list.isempty(list1))
                list.remove(list1, 2, true)
                return list.get(list1, 2)
               """);
        var i = L.PopInteger();
        Assert.Equal(5, i);
    }

    [Fact]
    public void TestNext1()
    {
        var L = new LuaState();
        L.OpenLibs();
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

    [Fact]
    public void TestPairs1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);
        
        L.Eval("""
               local list = require 'list'
               local list1 = list.new(1, 2, 3)
               local res = 0

               for i, v in list.pairs(list1) do
                 assert(list.get(list1, i) == v)
                 res += v
               end

               return res
               """);

        var i = L.PopInteger();
        Assert.Equal(6, i);
    }
    
    [Fact]
    public void TestPairs2()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);
        
        L.Eval("""
               local list = require 'list'
               local list1 = list.new(1, 2, 3)
               local res = 0

               for i, v in list1 do
                 assert(list.get(list1, i) == v)
                 res += v
               end

               return res
               """);

        var i = L.PopInteger();
        Assert.Equal(6, i);
    }
    
    [Fact]
    public void TestAddAll1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);
        
        L.Eval("""
               local list = require 'list'
               local list1 = list.new(1, 2, 3)
               local list2 = list.new(4, 5, 6)

               list.addall(list1, list2)

               return list1
               """);

        Assert.True(L.IsList(-1));
        var list = L.CheckList(-1);
        Assert.Equal(6, list.Count);
        
        for (var i = 0; i < list.Count; i++)
        {
            Assert.Equal(i + 1, list[i].NValue);
        }
    }
    
    [Fact]
    public void TestGetter1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);
        
        L.Eval("""
               local list = require 'list'
               local list1 = list.new(1, 2, 3)
               return list1[2]
               """);

        var i = L.PopInteger();
        Assert.Equal(3, i);
    }
    
    [Fact]
    public void TestSetter1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);
        
        L.Eval("""
               local list = require 'list'
               local list1 = list.new(1, 2, 3)
               list1[2] = 7
               return list1[2]
               """);

        var i = L.PopInteger();
        Assert.Equal(7, i);
    }
    
    [Fact]
    public void TestLen1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);
        
        L.Eval("""
               local list = require 'list'
               local list1 = list.new(1, 2, 3)
               return #list1
               """);

        var i = L.PopInteger();
        Assert.Equal(3, i);
    }
    
    [Fact]
    public void TestInsert1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaListLib.NameFuncPair, false);
        
        L.Eval("""
               local list = require 'list'
               local list1 = list.new(1, 2, 3)
               list.insert(list1, 1, 7)
               return list1[1]
               """);

        var i = L.PopInteger();
        Assert.Equal(7, i);
    }
}