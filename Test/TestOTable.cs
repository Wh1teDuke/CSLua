using CSLua;
using CSLua.Extensions;
using CSLua.Lib;
// ReSharper disable InconsistentNaming

namespace Test;

public sealed class TestOTable
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaOrderedTableLib.NameFuncPair, false);

        L.Eval("""
               local otable = require 'otable'
               local tab1 = otable.new()
               otable.set(tab1, "a", 1)
               otable.set(tab1, "b", 2)
               otable.set(tab1, "c", 3)
               otable.set(tab1, "d", 4)
               otable.set(tab1, "e", 5)

               assert(otable.haskey(tab1, "a"))
               assert(otable.hasval(tab1, 1))
               assert(otable.indexof(tab1, "a") == 0)
               assert(otable.length(tab1) == 5)

               otable.removeat(tab1, 0)

               assert(otable.length(tab1) == 4)
               assert(not otable.hasval(tab1, 1))
               assert(otable.indexof(tab1, "a") == -1)

               return otable.get(tab1, "e")
               """);

        var i = L.PopInteger();
        Assert.Equal(5, i);
    }
    
    [Fact]
    public void Test2()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaOrderedTableLib.NameFuncPair, false);

        L.Eval("""
                local otable = require 'otable'
                local tab1 = otable.new()
                assert(otable.isempty(tab1))
                otable.set(tab1, "a", 1)
                otable.set(tab1, "b", 2)
                otable.set(tab1, "c", 3)
                otable.set(tab1, "d", 4)
                assert(not otable.isempty(tab1))
                otable.removeat(tab1, 2)
                return otable.getat(tab1, 2)
               """);
        var i = L.PopInteger();
        Assert.Equal(4, i);
    }
    
    [Fact]
    public void TestNext1()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaOrderedTableLib.NameFuncPair, false);
        
        L.Eval("""
               local otable = require 'otable'
               local tab1 = otable.new()
               otable.set(tab1, "a", 1)
               otable.set(tab1, "b", 2)
               otable.set(tab1, "c", 3)

               local i, v = otable.next(tab1)
               local res = 0

               while i do
                 res += v
                 i, v = otable.next(tab1, i)
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
        L.Open(LuaOrderedTableLib.NameFuncPair, false);
        
        L.Eval("""
               local otable = require 'otable'
               local tab1 = otable.new()
               otable.set(tab1, "a", 1)
               otable.set(tab1, "b", 2)
               otable.set(tab1, "c", 3)

               local res = 0

               for i, v in otable.pairs(tab1) do
                 assert(otable.getat(tab1, i) == v)
                 res += v
               end

               return res
               """);

        var i = L.PopInteger();
        Assert.Equal(6, i);
    }

    [Fact]
    public void TestPairsOrder()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaOrderedTableLib.NameFuncPair, false);
        L.Open(LuaListLib.NameFuncPair, false);
        L.Eval("""
               local otable = require 'otable'
               local list = require 'list'

               local tab1 = otable.new()
               otable.set(tab1, "a", 1)
               otable.set(tab1, "b", 2)
               otable.set(tab1, "c", 3)
               
               local i = 0
               for _, v in otable.pairs(tab1) do
                 assert(otable.getat(tab1, i) == v)
                 i += 1
               end
               
               local list1 = list.new()
               for _, v in otable.pairs(tab1) do
                 list.add(list1, v)
               end
               
               for i, v in otable.pairs(tab1) do
                 assert(list1[i] == v)
               end
               """);
    }
    
    [Fact]
    public void TestPairsDelOrder()
    {
        var L = new LuaState();
        L.OpenLibs();
        L.Open(LuaOrderedTableLib.NameFuncPair, false);
        L.Eval("""
               local otable = require 'otable'

               local tab1 = otable.new()
               otable.set(tab1, "a", 1)
               otable.set(tab1, "b", 2)
               otable.set(tab1, "c", 3)
               
               otable.remove(tab1, "b")

               local lastidx = -1
               for i, v in otable.pairs(tab1) do
                 assert(i > lastidx)
                 lastidx = i
               end
               """);
    }
}