using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Linq.Expressions;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
// ReSharper disable UnusedType.Local

namespace Serilog.Settings.Configuration.Tests;

public class ObjectArgumentValueTests
{
    readonly IConfigurationRoot _config;

    public ObjectArgumentValueTests()
    {
        _config = new ConfigurationBuilder()
            .AddJsonFile("ObjectArgumentValueTests.json")
            .Build();
    }

    [Theory]
    [InlineData("case_1", typeof(A), "new A(1, 23:59:59, http://dot.com/, \"d\")")]
    [InlineData("case_2", typeof(B), "new B(2, new A(3, new D()), null)")]
    [InlineData("case_3", typeof(E), "new E(\"1\", \"2\", \"3\")")]
    [InlineData("case_4", typeof(F), "new F(\"paramType\", new E(1, 2, 3, 4))")]
    [InlineData("case_5", typeof(G), "new G()")]
    [InlineData("case_6", typeof(G), "new G(3, 4)")]
    [InlineData("case_7", typeof(H), "new H(new [] {\"1\", \"2\"})")]
    [InlineData("case_8", typeof(H), "new H(new [] {new D(), new I()})")]
    [InlineData("case_9", typeof(H), "new H(new J`1() {Void Add(C)(new D()), Void Add(C)(new I())})")]
    public void ShouldBindToConstructorArguments(string caseSection, Type targetType, string expectedExpression)
    {
        var testSection = _config.GetSection(caseSection);

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, targetType, new(), out var ctorExpression));
        Assert.Equal(expectedExpression, ctorExpression.ToString());
    }

    [Fact]
    public void ShouldBindToConstructorConcreteContainerArguments()
    {
        var testSection = _config.GetSection("case_9");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        Assert.IsType<J<C>>(instance.Concrete);
        Assert.Collection(instance.Concrete,
            first => Assert.IsType<D>(first),
            second => Assert.IsType<I>(second));
    }

    [Fact]
    public void ShouldBindToConstructorEnumerableArguments()
    {
        var testSection = _config.GetSection("case_9_enumerable");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        Assert.IsType<List<C>>(instance.Enumerable);
        Assert.Collection(instance.Enumerable,
            first => Assert.IsType<D>(first),
            second => Assert.IsType<I>(second));
    }

    [Fact]
    public void ShouldBindToConstructorEnumerableArgumentsWithExplicitStructImplementation()
    {
        var testSection = _config.GetSection("case_9_enumerable_explicit_struct_implementation");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        Assert.IsType<ArraySegment<C>>(instance.Enumerable);
        Assert.Collection(instance.Enumerable,
            first => Assert.IsType<D>(first),
            second => Assert.IsType<I>(second));
    }

    [Fact]
    public void ShouldBindToConstructorCollectionArguments()
    {
        var testSection = _config.GetSection("case_9_collection");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        Assert.IsType<List<C>>(instance.Collection);
        Assert.Collection(instance.Collection,
            first => Assert.IsType<D>(first),
            second => Assert.IsType<I>(second));
    }

    [Fact]
    public void ShouldBindToConstructorReadOnlyCollectionArguments()
    {
        var testSection = _config.GetSection("case_9_readOnlyCollection");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        Assert.IsType<List<C>>(instance.ReadOnlyCollection);
        Assert.Collection(instance.ReadOnlyCollection,
            first => Assert.IsType<D>(first),
            second => Assert.IsType<I>(second));
    }

    [Fact]
    public void ShouldBindToConstructorListArguments()
    {
        var testSection = _config.GetSection("case_9_list");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        Assert.IsType<List<C>>(instance.List);
        Assert.Collection(instance.List,
            first => Assert.IsType<D>(first),
            second => Assert.IsType<I>(second));
    }

    [Fact]
    public void ShouldBindToConstructorReadOnlyListArguments()
    {
        var testSection = _config.GetSection("case_9_readOnlyList");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        Assert.IsType<List<C>>(instance.ReadOnlyList);
        Assert.Collection(instance.ReadOnlyList,
            first => Assert.IsType<D>(first),
            second => Assert.IsType<I>(second));
    }

    [Fact]
    public void ShouldBindToConstructorSetArguments()
    {
        var testSection = _config.GetSection("case_9_set");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        var set = Assert.IsType<HashSet<C>>(instance.Set);
        Assert.Contains(set, item => item is D);
        Assert.Contains(set, item => item is I);
    }

#if NET5_0_OR_GREATER
    [Fact]
    public void ShouldBindToConstructorReadOnlySetArguments()
    {
        var testSection = _config.GetSection("case_9_readOnlySet");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        var readOnlySet = Assert.IsType<HashSet<C>>(instance.ReadOnlySet);
        Assert.Contains(readOnlySet, item => item is D);
        Assert.Contains(readOnlySet, item => item is I);
    }
#endif

    [Fact]
    public void ShouldBindToConstructorDictionaryArguments()
    {
        var testSection = _config.GetSection("case_9_dictionary");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        var dictionary = Assert.IsType<Dictionary<string, C>>(instance.Dictionary);
        Assert.IsType<D>(dictionary["a"]);
        Assert.IsType<I>(dictionary["b"]);
    }

    [Fact]
    public void ShouldBindToConstructorReadOnlyDictionaryArguments()
    {
        var testSection = _config.GetSection("case_9_readOnlyDictionary");

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        var readOnlyDictionary = Assert.IsType<Dictionary<string, C>>(instance.ReadOnlyDictionary);
        Assert.IsType<D>(readOnlyDictionary["a"]);
        Assert.IsType<I>(readOnlyDictionary["b"]);
    }

    [Fact]
    public void ShouldBindToConstructorDictionaryArgumentsWithExplicitImplementation()
    {
#if NETFRAMEWORK
        // SortedDictionary is in a different assembly in .Net Framework
        var testSection = _config.GetSection("case_9_dictionary_explicit_implementation_net_framework");
#else
        var testSection = _config.GetSection("case_9_dictionary_explicit_implementation");
#endif

        Assert.True(ObjectArgumentValue.TryBuildCtorExpression(testSection, typeof(H), new(), out var ctorExpression));
        var instance = Expression.Lambda<Func<H>>(ctorExpression).Compile()();
        var dictionary = Assert.IsType<SortedDictionary<string, C>>(instance.Dictionary);
        Assert.IsType<D>(dictionary["a"]);
        Assert.IsType<I>(dictionary["b"]);
    }

    class A
    {
        public A(int a, TimeSpan b, Uri c, string d = "d") { }
        public A(int a, C c) { }
    }

    class B
    {
        public B(int b, A a, long? c = null) { }
    }

    interface C { }

    class D : C { }

    class E
    {
        public E(int a, int b, int c, int d = 4) { }
        public E(int a, string b, string c) { }
        public E(string a, string b, string c) { }
    }

    class F
    {
        public F(string type, E e) { }
    }

    class G
    {
        public G() { }
        public G(int a = 1, int b = 2) { }
    }

    class H
    {
        public J<C>? Concrete { get; }
        public IEnumerable<C>? Enumerable { get; }
        public ICollection<C>? Collection { get; }
        public IReadOnlyCollection<C>? ReadOnlyCollection { get; }
        public IList<C>? List { get; }
        public IReadOnlyList<C>? ReadOnlyList { get; }
        public ISet<C>? Set { get; }
#if NET5_0_OR_GREATER
        public IReadOnlySet<C>? ReadOnlySet { get; }
#endif
        public IDictionary<string, C>? Dictionary { get; }
        public IReadOnlyDictionary<string, C>? ReadOnlyDictionary { get; }

        public H(params string[] strings) { }
        public H(C[] array) { }
        public H(J<C> concrete) { Concrete = concrete; }
        public H(IEnumerable<C> enumerable) { Enumerable = enumerable; }
        public H(ICollection<C> collection) { Collection = collection; }
        public H(IReadOnlyCollection<C> readOnlyCollection) { ReadOnlyCollection = readOnlyCollection; }
        public H(IList<C> list) { List = list; }
        public H(IReadOnlyList<C> readOnlyList) { ReadOnlyList = readOnlyList; }
        public H(ISet<C> set) { Set = set; }
#if NET5_0_OR_GREATER
        public H(IReadOnlySet<C> readOnlySet) { ReadOnlySet = readOnlySet; }
#endif
        public H(IDictionary<string, C> dictionary) { Dictionary = dictionary; }
        public H(IReadOnlyDictionary<string, C> readOnlyDictionary) { ReadOnlyDictionary = readOnlyDictionary; }
    }

    class I : C { }

    class J<T> : IEnumerable<T>
    {
        private List<T> list = new();
        public void Add(T value)
        {
            list.Add(value);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return list.GetEnumerator();
        }
    }
}
