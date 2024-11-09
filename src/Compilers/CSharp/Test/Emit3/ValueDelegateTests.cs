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
            // PROTOTYPE: How would this look with associated types, Compare<TComparer, implicit T>(...),
            // and how can we test that?
            string source = """
                using System;
                using System.Collections.Generic;
                ref struct Comparer<T> : IComparer<T>
                    where T : IComparable<T>
                {
                    public int Compare(T x, T y)
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
                        int result = Compare("Hello", "World", new Comparer<string>());
                        Console.WriteLine(result);
                    }
                }
                """;
            CompileAndVerify(source, targetFramework: TargetFramework.Net90, verify: Verification.Skipped, expectedOutput: IncludeExpectedOutput("-1"));
        }

        [Fact]
        public void RefStructReturn()
        {
            string source = """
                using System;
                interface I
                {
                    void F(ref int i);
                }
                ref struct R : I
                {
                    private ref int _i;
                    public void F(ref int i)
                    {
                        _i = ref i;
                    }
                    public override string ToString() => "R";
                }
                class Program
                {
                    static T F<T>() where T : new(), allows ref struct
                    {
                        return new T();
                    }
                    static void Main()
                    {
                        var r = F<R>();
                        Console.WriteLine(r.ToString());
                    }
                }
                """;
            CompileAndVerify(source, targetFramework: TargetFramework.Net90, verify: Verification.Skipped, expectedOutput: IncludeExpectedOutput("R"));
        }

        // PROTOTYPE: Required changes:
        // * Support lambdas for single functional interfaces.
        //   - Generate struct for delegate and closure.
        //   - Generate ref struct closure if captured variables are mutable, where the captured variables are passed by reference, and if the corresponding type parameter allows ref structs.
        // * Partial type inference.
        //   - Needed since the method being called may have generic type parameters in addition to the interface (delegate) type
        //     and the synthesized delegate type has an unspeakable name so that type argument cannot be explicit.
        // * Additional ref analysis.
        //   - Ref to ref struct may be needed for nested closures, at least ref readonly.
        //   - Ref assigning to captured ref local for instance (see RefStructReturn test above).
        //   - Nested IFastEnumerable instances (see LINQ below) may need to be refs rather than values (although not necessarily if each ref struct is a return value).
        //   - If we aren't ready for full ref annotations for these cases, perhaps we just need to skip ref analysis for compiler-generated closures.
        //
        // Additionally, for LINQ:
        // * Fast IEnumerable interface and recognize in C#.
        //   - Existing IEnumerable implementations should implement IFastEnumerable.
        //   - Compiler treats array (and string) as implementing IFastEnumerable, or is this handled by the BCL?
        // * Fast LINQ implementation.
        //   - Implementation returns ref structs. Not usable for LINQ use where the resulting IEnumerable will be copied to the heap.
        // * Support associated types.
        //   - Needed for type inference on Where() etc.

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
                namespace System
                {
                    public interface IFunc<T, TResult>
                    {
                        TResult Invoke(T t);
                    }
                }
                """;
            string sourceC = """
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
            string sourceD = """
                using System.Collections.Generic;

                namespace System.Linq
                {
                    public readonly ref struct SelectEnumerable<TSourceElement, TSourceEnumerable, TResultElement, TSelector, [ImplicitTypeArgument] TSourceEnumerator> :
                        IFastEnumerable<TResultElement, TSourceEnumerator>
                        where TSourceEnumerable : IFastEnumerable<TSourceElement, TSourceEnumerator>, allows ref struct
                        where TSelector : IFunc<TSourceElement, TResultElement>, allows ref struct
                    {
                        private readonly TSourceEnumerable _source;
                        private readonly TSelector _selector;

                        public SelectEnumerable(TSourceEnumerable source, TSelector selector)
                        {
                            _source = source;
                            _selector = selector;
                        }

                        public TSourceEnumerator Start => _source.Start;

                        public bool TryGetNext(ref TSourceEnumerator sourceEnumerator, out TResultElement value)
                        {
                            if (_source.TryGetNext(ref sourceEnumerator, out var sourceElement))
                            {
                                value = _selector.Invoke(sourceElement);
                                return true;
                            }
                            value = default;
                            return false;
                        }
                    }

                    public readonly ref struct WhereEnumerable<TSourceElement, TSourceEnumerable, TPredicate, [ImplicitTypeArgument] TSourceEnumerator> :
                        IFastEnumerable<TSourceElement, TSourceEnumerator>
                        where TSourceEnumerable : IFastEnumerable<TSourceElement, TSourceEnumerator>, allows ref struct
                        where TPredicate : IFunc<TSourceElement, bool>, allows ref struct
                    {
                        private readonly TSourceEnumerable _source;
                        private readonly TPredicate _predicate;

                        public WhereEnumerable(TSourceEnumerable source, TPredicate predicate)
                        {
                            _source = source;
                            _predicate = predicate;
                        }

                        public TSourceEnumerator Start => _source.Start;

                        public bool TryGetNext(ref TSourceEnumerator sourceEnumerator, out TSourceElement value)
                        {
                            while (_source.TryGetNext(ref sourceEnumerator, out var sourceElement))
                            {
                                if (_predicate.Invoke(sourceElement))
                                {
                                    value = sourceElement;
                                    return true;
                                }
                            }
                            value = default;
                            return false;
                        }
                    }

                    public static class Enumerable
                    {
                        public static SelectEnumerable<TSourceElement, TSourceEnumerable, TResultElement, TSelector, TSourceEnumerator> Select<TSourceElement, TSourceEnumerable, TResultElement, TSelector, [ImplicitTypeArgument] TSourceEnumerator>(
                            this TSourceEnumerable source,
                            TSelector selector)
                            where TSourceEnumerable : IFastEnumerable<TSourceElement, TSourceEnumerator>, allows ref struct
                            where TSelector : IFunc<TSourceElement, TResultElement>, allows ref struct
                        {
                            return new(source, selector);
                        }

                        public static WhereEnumerable<TSourceElement, TSourceEnumerable, TPredicate, TSourceEnumerator> Where<TSourceElement, TSourceEnumerable, TPredicate, [ImplicitTypeArgument] TSourceEnumerator>(
                            this TSourceEnumerable source,
                            TPredicate predicate)
                            where TSourceEnumerable : IFastEnumerable<TSourceElement, TSourceEnumerator>, allows ref struct
                            where TPredicate : IFunc<TSourceElement, bool>, allows ref struct
                        {
                            return new(source, predicate);
                        }
                    }
                }
                """;
            string sourceE = """
                using System.Collections.Generic;

                // We're using a ref struct to validate that instances are not boxed,
                // not because the ref struct contains references.
                readonly ref struct ArrayEnumerable<T> :
                    IFastEnumerable<T, int>
                {
                    private readonly T[] _array;

                    public ArrayEnumerable(T[] array)
                    {
                        _array = array;
                    }

                    public int Start => 0;

                    public bool TryGetNext(ref int index, out T value)
                    {
                        if (index < _array.Length)
                        {
                            value = _array[index];
                            index++;
                            return true;
                        }
                        value = default;
                        return false;
                    }
                }
                """;
            string sourceF = """
                using System;
                using System.Linq;

                ref struct WhereByteOddPredicate : IFunc<byte, bool>
                {
                    public bool Invoke(byte b) => b % 2 != 0;
                }

                ref struct WhereShortGreaterThanOne : IFunc<short, bool>
                {
                    public bool Invoke(short s) => s > 1;
                }

                ref struct SelectByteAsShort : IFunc<byte, short>
                {
                    public short Invoke(byte b) => b;
                }

                class Program
                {
                    static void Main()
                    {
                        var x = new ArrayEnumerable<byte>([1, 2, 3]);
                        // PROTOTYPE: Shouldn't be necessary to specify type arguments to Select() or Where().
                        var y = x.
                            Where<byte, ArrayEnumerable<byte>, WhereByteOddPredicate, int>(new WhereByteOddPredicate()).
                            Select<byte, WhereEnumerable<byte, ArrayEnumerable<byte>, WhereByteOddPredicate, int>, short, SelectByteAsShort, int>(new SelectByteAsShort()).
                            Where<short, SelectEnumerable<byte, WhereEnumerable<byte, ArrayEnumerable<byte>, WhereByteOddPredicate, int>, short, SelectByteAsShort, int>, WhereShortGreaterThanOne, int>(new WhereShortGreaterThanOne());

                        for (var e = y.Start; y.TryGetNext(ref e, out var i);)
                            Console.Write("{0}, ", i);
                    }
                }
                """;
            CompileAndVerify(
                [sourceA, sourceB, sourceC, sourceD, sourceE, sourceF],
                targetFramework: TargetFramework.Net90,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("3, "));
        }

        // PROTOTYPE: Test capturing and modifying a ref struct local.
    }
}
