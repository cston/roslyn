// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public class ValueDelegateTests : CSharpTestBase
    {
        private static string IncludeExpectedOutput(string expectedOutput) => ExecutionConditionUtil.IsMonoOrCoreClr ? expectedOutput : null;

        [Fact]
        public void Example()
        {
            // PROTOTYPE: How would this look with associated types, Comparer<TComparer, implicit T>(...),
            // and how can we test that?
            string source = """
                using System;
                using System.Collections.Generic;
                ref struct Comparer : IComparer<string>
                {
                    public int Compare(string x, string y)
                    {
                        return x.CompareTo(y);
                    }
                }
                class Program
                {
                    static int Compare<TComparer, T>(T x, T y, TComparer comparer)
                        where TComparer : IComparer<T>, allows ref struct
                    {
                        return comparer.Compare(x, y);
                    }
                    static void Main()
                    {
                        int result = Compare("Hello", "World", new Comparer());
                        Console.WriteLine(result);
                    }
                }
                """;
            CompileAndVerify(source, targetFramework: TargetFramework.Net90, verify: Verification.Skipped, expectedOutput: IncludeExpectedOutput("-1"));
        }

        // PROTOTYPE: Test Where() with value delegates.
        [Fact]
        public void LINQ_01()
        {
            string sourceA = """
                namespace System
                {
                    public sealed class ImplicitTypeArgumentAttribute : Attribute
                    {
                    }
                }
                """;
            string sourceB = """
                namespace System.Collections.Generic
                {
                    public interface IFastEnumerable<TElement, [ImplicitTypeArgument] TEnumerator>
                        where TEnumerator : allows ref struct
                    {
                        TEnumerator Start { get; }
                        bool TryGetNext(ref TEnumerator enumerator, out TElement value);
                    }
                }
                """;
            // PROTOTYPE: Having a unique signature, with perhaps a unique associated type, for each Enumerable
            // method doesn't scale. It also doesn't makes it difficult to version the interface because the number
            // of type parameters might increase with later versions.
            string sourceC = """
                using System.Collections.Generic;

                namespace System.Linq
                {
                    public ref struct SelectResultEnumerable<TSourceEnumerable, TElement, [ImplicitTypeArgument] TSourceEnumerator> : IFastEnumerable<TElement, ResultEnumerator>
                    {
                        private readonly TSourceEnumerable Source;

                        public ResultEnumerator Start => 
                        public ref struct ResultEnumerator
                        {
                            public readonly TSourceEnumerator SourceEnumerator;

                        }
                    }

                    public interface ISourceEnumerable<TElement, [ImplicitTypeArgument] TEnumerator, [ImplicitTypeArgument] TResultEnumerable> : IFastEnumerable<TElement, TEnumerator>
                        where TResultEnumerable : allows ref struct
                    {
                        // PROTOTYPE: If we have a Where(predicate) method here, why do we need Enumerable.Where()?
                        TResultEnumerable Where(Func<TElement, bool> predicate);
                        T
                    }

                    public static class Enumerable
                    {
                        public static TResultEnumerable Where<TSourceEnumerable, TElement, [ImplicitTypeArgument] TSourceEnumerator, [ImplicitTypeArgument] TResultEnumerable>(
                            this TSourceEnumerable source,
                            Func<TElement, bool> predicate)
                            where TSourceEnumerable : ISourceEnumerable<TElement, TSourceEnumerable, TResultEnumerable>
                        {
                            return source.Where(predicate);
                        }
                    }
                }
                """;
            string sourceD = """
                using System;
                using System.Linq;

                // We're using a ref struct to validate that instances are not boxed,
                // not because the ref struct contains references.
                readonly ref struct ArrayEnumerable<T> : ISourceEnumerable<T, int, ArrayEnumerable<T>>
                {
                    private readonly T[] _array;
                    private readonly Func<T, bool> _predicate;

                    public ArrayEnumerable(T[] array, Func<T, bool> predicate = null)
                    {
                        _array = array;
                        _predicate = predicate ?? (_ => true);
                    }

                    public int Start => 0;

                    public bool TryGetNext(ref int index, out T value)
                    {
                        while (index < _array.Length)
                        {
                            T t = _array[index];
                            index++;
                            if (_predicate(t))
                            {
                                value = t;
                                return true;
                            }
                        }
                        value = default;
                        return false;
                    }

                    public ArrayEnumerable<T> Where(Func<T, bool> predicate)
                    {
                        var thisPredicate = _predicate;
                        return new ArrayEnumerable<T>(_array, t => thisPredicate(t) && predicate(t));
                    }
                }
                """;
            string sourceE = """
                using System;

                class Program
                {
                    static void Main()
                    {
                        var x = new ArrayEnumerable<int>([1, 2, 3]);
                        var y = x.Where(i => i % 2 != 0).Where(i => i > 1);

                        for (var e = y.Start; y.TryGetNext(ref e, out var i); )
                            Console.Write("{0}, ", i);
                    }
                }
                """;
            CompileAndVerify(
                [sourceA, sourceB, sourceC, sourceD, sourceE],
                targetFramework: TargetFramework.Net90,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("3, "));
        }

        // PROTOTYPE: Test capturing and modifying a ref struct local.
    }
}
