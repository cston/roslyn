// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public class RefFieldTests : CSharpTestBase
    {
        private static string IncludeExpectedOutput(string expectedOutput)
        {
            // PROTOTYPE: Enable.
#if RuntimeSupport
            return expectedOutput;
#else
            return null;
#endif
        }

        [CombinatorialData]
        [Theory]
        public void RefField(bool useCompilationReference)
        {
            var sourceA =
@"public ref struct S<T>
{
    public ref T F;
    public ref T F1() => throw null;
    public S(ref T t)
    {
        F = t;
    }
}";
            var comp = CreateCompilation(sourceA, parseOptions: TestOptions.Regular10);
            comp.VerifyDiagnostics(
                // (3,12): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     public ref T F;
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "ref T").WithArguments("ref fields").WithLocation(3, 12));

            var field = comp.GetMember<FieldSymbol>("S.F");
            Assert.Equal(RefKind.Ref, field.RefKind);

            comp = CreateCompilation(sourceA);
            comp.VerifyDiagnostics();
            var refA = AsReference(comp, useCompilationReference);

            var sourceB =
@"using System;
class Program
{
    static void Main()
    {
        int x = 1;
        var s = new S<int>(ref x);
        s.F = 2;
        Console.WriteLine(s.F);
        Console.WriteLine(x);
        x = 3;
        Console.WriteLine(s.F);
        Console.WriteLine(x);
    }
}";
            // PROTOTYPE: Should use of ref field be tied to -langversion:preview?
            var verifier = CompileAndVerify(sourceB, references: new[] { refA }, parseOptions: TestOptions.RegularNext, verify: Verification.Skipped, expectedOutput: IncludeExpectedOutput(
@"2
2
3
3
"));
            comp = (CSharpCompilation)verifier.Compilation;

            field = comp.GetMember<FieldSymbol>("S.F");
            Assert.Equal(RefKind.Ref, field.RefKind);

            verifier.VerifyIL("Program.Main",
@"{
  // Code size       55 (0x37)
  .maxstack  2
  .locals init (int V_0, //x
                S<int> V_1) //s
  IL_0000:  ldc.i4.1
  IL_0001:  stloc.0
  IL_0002:  ldloca.s   V_0
  IL_0004:  newobj     ""S<int>..ctor(ref int)""
  IL_0009:  stloc.1
  IL_000a:  ldloca.s   V_1
  IL_000c:  ldc.i4.2
  IL_000d:  stfld      ""int S<int>.F""
  IL_0012:  ldloc.1
  IL_0013:  ldfld      ""int S<int>.F""
  IL_0018:  call       ""void System.Console.WriteLine(int)""
  IL_001d:  ldloc.0
  IL_001e:  call       ""void System.Console.WriteLine(int)""
  IL_0023:  ldc.i4.3
  IL_0024:  stloc.0
  IL_0025:  ldloc.1
  IL_0026:  ldfld      ""int S<int>.F""
  IL_002b:  call       ""void System.Console.WriteLine(int)""
  IL_0030:  ldloc.0
  IL_0031:  call       ""void System.Console.WriteLine(int)""
  IL_0036:  ret
}");
        }

        [Fact]
        public void RefAutoProperty()
        {
            var source =
@"ref struct S<T>
{
    ref T P { get; }
    public S(ref T t)
    {
        P = t;
    }
}";
            var comp = CreateCompilation(source);
            // PROTOTYPE: Should this scenario be supported?
            comp.VerifyDiagnostics(
                // (3,11): error CS8145: Auto-implemented properties cannot return by reference
                //     ref T P { get; }
                Diagnostic(ErrorCode.ERR_AutoPropertyCannotBeRefReturning, "P").WithArguments("S<T>.P").WithLocation(3, 11));
        }

        [Fact]
        public void RefFieldAssignment()
        {
            var source =
@"struct S<T>
{
    T F;
    ref T R;
    S(T t)
    {
        F = t;
        R = ref F;
    }
}";
            var comp = CreateCompilation(source);
            // PROTOTYPE: Should this scenario be supported?
            comp.VerifyDiagnostics(
                // (8,9): error CS8373: The left-hand side of a ref assignment must be a ref local or parameter.
                //         R = ref F;
                Diagnostic(ErrorCode.ERR_RefLocalOrParamExpected, "R").WithLocation(8, 9),
                // (8,9): error CS0170: Use of possibly unassigned field 'R'
                //         R = ref F;
                Diagnostic(ErrorCode.ERR_UseDefViolationField, "R").WithArguments("R").WithLocation(8, 9));
        }

        [Fact]
        public void ParameterScope_01()
        {
            var source =
@"class Program
{
    static void F1(scoped int x1, unscoped object y1) { }
    static void F2(scoped ref int x2, unscoped ref object y2) { }
    static void F3(scoped in int x3, unscoped in object y3) { }
    static void F4(scoped out int x4, unscoped out object y4) { x4 = 0; y4 = 0; }
}";

            var comp = CreateCompilation(source, parseOptions: TestOptions.Regular10);
            comp.VerifyDiagnostics(
                // (3,20): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     static void F1(scoped int x1, unscoped object y1) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(3, 20),
                // (3,35): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     static void F1(scoped int x1, unscoped object y1) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(3, 35),
                // (4,20): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     static void F2(scoped ref int x2, unscoped ref object y2) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(4, 20),
                // (4,39): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     static void F2(scoped ref int x2, unscoped ref object y2) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(4, 39),
                // (5,20): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     static void F3(scoped in int x3, unscoped in object y3) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(5, 20),
                // (5,38): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     static void F3(scoped in int x3, unscoped in object y3) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(5, 38),
                // (6,20): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     static void F4(scoped out int x4, unscoped out object y4) { x4 = 0; y4 = 0; }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(6, 20),
                // (6,39): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     static void F4(scoped out int x4, unscoped out object y4) { x4 = 0; y4 = 0; }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(6, 39));

            comp = CreateCompilation(source, parseOptions: TestOptions.RegularNext);
            comp.VerifyDiagnostics();
        }

        [Fact]
        public void ParameterScope_02()
        {
            var source =
@"struct A<T>
{
    A(unscoped ref T t) { }
    A(scoped ref object o) { }
    T this[unscoped in object o] => default;
    public static implicit operator B<T>(scoped in A<T> a) => default;
}
struct B<T>
{
}";

            var comp = CreateCompilation(source, parseOptions: TestOptions.Regular10);
            comp.VerifyDiagnostics(
                // (3,7): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     A(unscoped ref T t) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(3, 7),
                // (4,7): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     A(scoped ref object o) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(4, 7),
                // (5,12): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     T this[unscoped in object o] => default;
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(5, 12),
                // (6,42): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //     public static implicit operator B<T>(scoped in A<T> a) => default;
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(6, 42));

            comp = CreateCompilation(source, parseOptions: TestOptions.RegularNext);
            comp.VerifyDiagnostics();
        }

        [Fact]
        public void ParameterScope_03()
        {
            var source =
@"class Program
{
    static void Main()
    {
#pragma warning disable 8321
        static void F1(scoped int x1, unscoped object y1) { }
        static void F2(scoped ref int x2, unscoped ref object y2) { }
        static void F3(scoped in int x3, unscoped in object y3) { }
        static void F4(scoped out int x4, unscoped out object y4) { x4 = 0; y4 = 0; }
    }
}";

            var comp = CreateCompilation(source, parseOptions: TestOptions.Regular10);
            comp.VerifyDiagnostics(
                // (6,24): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         static void F1(scoped int x1, unscoped object y1) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(6, 24),
                // (6,39): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         static void F1(scoped int x1, unscoped object y1) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(6, 39),
                // (7,24): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         static void F2(scoped ref int x2, unscoped ref object y2) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(7, 24),
                // (7,43): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         static void F2(scoped ref int x2, unscoped ref object y2) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(7, 43),
                // (8,24): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         static void F3(scoped in int x3, unscoped in object y3) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(8, 24),
                // (8,42): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         static void F3(scoped in int x3, unscoped in object y3) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(8, 42),
                // (9,24): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         static void F4(scoped out int x4, unscoped out object y4) { x4 = 0; y4 = 0; }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(9, 24),
                // (9,43): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         static void F4(scoped out int x4, unscoped out object y4) { x4 = 0; y4 = 0; }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(9, 43));

            comp = CreateCompilation(source, parseOptions: TestOptions.RegularNext);
            comp.VerifyDiagnostics();
        }

        [Fact]
        public void MethodScope_01()
        {
            var source =
@"class Program
{
    static void Main()
    {
#pragma warning disable 8321
        static scoped ref T F1<T>() => throw null;
        static unscoped ref T F2<T>() => throw null;
    }
}";

            var comp = CreateCompilation(source, parseOptions: TestOptions.Regular10);
            comp.VerifyDiagnostics(
                // (6,24): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         static void F1(scoped int x1, unscoped object y1) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(6, 24),
                // (6,39): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         static void F1(scoped int x1, unscoped object y1) { }
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(6, 39));

            comp = CreateCompilation(source, parseOptions: TestOptions.RegularNext);
            comp.VerifyDiagnostics();
        }

        // PROTOTYPE: '[RefThisEscapes]' can be applied to a type, or a method, or an accessor (see proposal).

        // PROTOTYPE: Test method with explicit scope:
        // - partial method
        // - accessor

        // PROTOTYPE: Test type with explicit scope:
        // - class, struct, record, delegate, enum
        // - partial type
        // - as type argument

        // PROTOTYPE: Test local with explicit scope:
        // - const

        // PROTOTYPE: Test value with explicit scope:
        // - in all expression types in Binder.CheckValEscape()

        [Fact]
        public void ParameterScope_04()
        {
            var source =
@"class Program
{
    static void Main()
    {
        var f1 = (scoped int x1, unscoped object y1) => { };
        var f2 = (scoped ref int x2, unscoped ref object y2) => { };
        var f3 = (scoped in int x3, unscoped in object y3) => { };
        var f4 = (scoped out int x4, unscoped out object y4) => { x4 = 0; y4 = 0; };
    }
}";

            var comp = CreateCompilation(source, parseOptions: TestOptions.Regular10);
            // PROTOTYPE: Should report ErrorCode.ERR_FeatureInPreview for 'scoped' and 'unscoped'.
            // (Should call ParameterHelpers.CheckParameterModifiers() for lambda parameters.)
            comp.VerifyDiagnostics();

            comp = CreateCompilation(source, parseOptions: TestOptions.RegularNext);
            comp.VerifyDiagnostics();
        }

        [Fact]
        public void ParameterScope_05()
        {
            var source =
@"delegate void D1<T>(scoped ref T t);
delegate void D2<T>(unscoped in T t);
";

            var comp = CreateCompilation(source, parseOptions: TestOptions.Regular10);
            comp.VerifyDiagnostics(
                // (1,21): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                // delegate void D1<T>(scoped ref T t);
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(1, 21),
                // (2,21): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                // delegate void D2<T>(unscoped in T t);
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "unscoped").WithArguments("ref fields").WithLocation(2, 21));

            comp = CreateCompilation(source, parseOptions: TestOptions.RegularNext);
            comp.VerifyDiagnostics();
        }

        [Fact]
        public void LocalScope_01()
        {
            var source =
@"class Program
{
    static void Main()
    {
        scoped int x1 = 0, y1 = 0;
        scoped ref int x2 = ref x1, y2 = ref y1;
    }
}";

            var comp = CreateCompilation(source, parseOptions: TestOptions.Regular10);
            comp.VerifyDiagnostics(
                // (5,9): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         scoped int x1 = 0, y1 = 0;
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(5, 9),
                // (6,9): error CS8652: The feature 'ref fields' is currently in Preview and *unsupported*. To use Preview features, use the 'preview' language version.
                //         scoped ref int x2 = ref x1, y2 = ref y1;
                Diagnostic(ErrorCode.ERR_FeatureInPreview, "scoped").WithArguments("ref fields").WithLocation(6, 9));

            comp = CreateCompilation(source, parseOptions: TestOptions.RegularNext);
            comp.VerifyDiagnostics();
        }

        // PROTOTYPE: Test that unsafe is no longer needed to pass a ref struct by ref
        // in the 'DangerousCode()' example in the proposal.

        // Based on first [DoesNotEscape] example in proposal.
        [Fact]
        public void DoesNotEscape_01()
        {
            var source =
@"ref struct R<T> { }
class Program
{
    static R<int> F0(R<int> r0)
    {
        return r0;
    }
    static R<int> F1(scoped R<int> r1)
    {
        return r1; // 1
    }
    static R<int> F2(scoped R<int> r2)
    {
        R<int> l2 = r2;
        return l2; // 2
    }
}";

            var comp = CreateCompilation(source, parseOptions: TestOptions.RegularNext);
            comp.VerifyDiagnostics(
                // (10,16): error CS8352: Cannot use l2 'R<int>' in this context because it may expose referenced variables outside of their declaration scope
                //         return r1; // 1
                Diagnostic(ErrorCode.ERR_EscapeLocal, "r1").WithArguments("R<int>").WithLocation(10, 16),
                // (15,16): error CS8352: Cannot use l2 'l2' in this context because it may expose referenced variables outside of their declaration scope
                //         return l2; // 2
                Diagnostic(ErrorCode.ERR_EscapeLocal, "l2").WithArguments("l2").WithLocation(15, 16));

            // PROTOTYPE: Test 'unscoped', assigning from { none, scoped, unscoped }.
            // PROTOTYPE: Test RefEscapeScope for the cases above.
        }

        [Fact]
        public void DoesNotEscape_02()
        {
            var source =
@"ref struct R<T> { }
class Program
{
    static R<int> F3(R<int> r3)
    {
        scoped R<int> l3 = r3;
        return l3; // 3
    }
}";

            var comp = CreateCompilation(source, parseOptions: TestOptions.RegularNext);
            comp.VerifyDiagnostics(
                // (7,16): error CS8352: Cannot use local 'l3' in this context because it may expose referenced variables outside of their declaration scope
                //         return l3; // 3
                Diagnostic(ErrorCode.ERR_EscapeLocal, "l3").WithArguments("l3").WithLocation(7, 16));
        }

        // PROTOTYPE: Test `scoped Span<int> local = p2;` above.

        // From spec:
        // Parameter        ref-safe-to-escape safe-to-escape
        // Span<T>            current method   calling method
        // scoped Span<T>     current method   current method
        // ref Span<T>        calling method   calling method
        // scoped ref Span<T> current method   calling method
        // ref scoped Span<T> current method   current method

        [Fact]
        public void RefSafeToEscape_02()
        {
            var source =
@"ref struct R<T>
{
    public ref T F;
    public R(ref T t) { F = t; }
}
class Program
{
    static R<T> F1<T>(R<T> r) => new R<T>(ref r.F);
    static R<T> F2<T>(scoped R<T> r) => new R<T>(ref r.F);
    static R<T> F3<T>(ref R<T> r) => new R<T>(ref r.F);
    static R<T> F4<T>(scoped ref R<T> r) => new R<T>(ref r.F);
    static R<T> F5<T>(ref scoped R<T> r) => new R<T>(ref r.F);
}";

            var comp = CreateCompilation(source, parseOptions: TestOptions.RegularNext);
            comp.VerifyDiagnostics();

            // PROTOTYPE: Verify ref-safe-to-escape value for each parameter.
            // PROTOTYPE: Add similar test for safe-to-escape
            // PROTOTYPE: Test 'this' escape.
        }

        // PROTOTYPE:
        // - Test scope of 'in' argument when the argument is a literal or rvalue. F(new object()) for F(unscoped in object o) for instance.

        // PROTOTYPE: What are the important differences between this branch and main?
        // - ref struct value has a ref-safe-to-escape that indicates how ref fields may escape
        // ...
    }
}
