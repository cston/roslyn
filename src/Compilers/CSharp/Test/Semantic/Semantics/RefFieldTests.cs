// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public class RefFieldTests : CSharpTestBase
    {
        [Theory]
        [InlineData(LanguageVersion.CSharp10)]
        [InlineData(LanguageVersionFacts.CSharpNext)]
        public void UnannotatedMethodWarning_StaticMethods_01(LanguageVersion langVersion)
        {
            var source =
@"struct S<T> { }
ref struct R<T> { }
class Program
{
    static S<T> F0<T>(ref T t) => throw null;
    static R<T> F1<T>() => throw null;
    static R<T> F2<T>(T t) => throw null;
    static R<T> F3<T>(ref T t) => throw null; // 1
    static R<T> F4<T>(in T t) => throw null; // 2
    static R<T> F5<T>(out T t) => throw null;
}";
            var comp = CreateCompilation(source, parseOptions: TestOptions.Regular.WithLanguageVersion(langVersion));
            comp.VerifyDiagnostics(
                // (8,17): warning CS9060: Method 'Program.F3<T>(ref T)' may capture a ref parameter in a ref struct field.
                //     static R<T> F3<T>(ref T t) => throw null; // 1
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F3").WithArguments("Program.F3<T>(ref T)").WithLocation(8, 17),
                // (9,17): warning CS9060: Method 'Program.F4<T>(in T)' may capture a ref parameter in a ref struct field.
                //     static R<T> F4<T>(in T t) => throw null; // 2
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F4").WithArguments("Program.F4<T>(in T)").WithLocation(9, 17));
        }

        [Fact]
        public void UnannotatedMethodWarning_StaticMethods_02()
        {
            var source =
@"struct S<T> { }
ref struct R<T> { }
class Program
{
    static void F0<T>(out S<T> s, ref T t) { }
    static void F1<T>(R<T> r) { }
    static void F2<T>(ref R<T> r) { }
    static void F3<T>(in R<T> r) { }
    static void F4<T>(out R<T> r) { }
    static void F5<T>(out R<T> r, T t) { }
    static void F6<T>(out R<T> r, ref T t) { } // 1
    static void F7<T>(out R<T> r, in T t) { } // 2
    static void F8<T>(out R<T> r, out T t) { t = default; }
    static void F9<T>(R<T> r, ref T t) { }
    static void FA<T>(ref R<T> r, ref T t) { } // 3
    static void FB<T>(in R<T> r, ref T t) { }
}";
            var comp = CreateCompilation(source);
            comp.VerifyDiagnostics(
                // (11,17): warning CS9060: Method 'Program.F6<T>(out R<T>, ref T)' may capture a ref parameter in a ref struct field.
                //     static void F6<T>(out R<T> r, ref T t) { } // 1
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F6").WithArguments("Program.F6<T>(out R<T>, ref T)").WithLocation(11, 17),
                // (12,17): warning CS9060: Method 'Program.F7<T>(out R<T>, in T)' may capture a ref parameter in a ref struct field.
                //     static void F7<T>(out R<T> r, in T t) { } // 2
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F7").WithArguments("Program.F7<T>(out R<T>, in T)").WithLocation(12, 17),
                // (15,17): warning CS9060: Method 'Program.FA<T>(ref R<T>, ref T)' may capture a ref parameter in a ref struct field.
                //     static void FA<T>(ref R<T> r, ref T t) { } // 3
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "FA").WithArguments("Program.FA<T>(ref R<T>, ref T)").WithLocation(15, 17));
        }

        [Fact]
        public void UnannotatedMethodWarning_StaticMethods_03()
        {
            var source =
@"struct S<T> { }
ref struct R<T> { }
class Program
{
    static ref S<T> F0<T>(ref T t) => throw null;
    static ref R<T> F1<T>(ref T t) => throw null;
    static ref readonly S<T> F2<T>(ref T t) => throw null;
    static ref readonly R<T> F3<T>(ref T t) => throw null;
}";
            var comp = CreateCompilation(source);
            comp.VerifyDiagnostics();
        }

        [Theory]
        [InlineData(LanguageVersion.CSharp10)]
        [InlineData(LanguageVersionFacts.CSharpNext)]
        public void UnannotatedMethodWarning_InstanceMethods_01(LanguageVersion langVersion)
        {
            var source =
@"ref struct R<T>
{
#pragma warning disable 649
    internal T F;
}
class C
{
    R<T> F0<T>() => throw null;
    R<T> F1<T>(T t) => throw null;
    R<T> F2<T>(ref T t) => throw null; // 1
    R<T> F3<T>(in T t) => throw null; // 2
    R<T> F4<T>(out T t) => throw null;
    void F5() { }
    void F6<T>(R<T> r) { }
    void F7<T>(ref R<T> r) { }
    void F8<T>(in R<T> r) { }
    void F9<T>(out R<T> r) { r = default; }
    void F10<T>(out R<T> r, T t) { r = default; }
    void F11<T>(out R<T> r, ref T t) { r = default; } // 3
    void F12<T>(out R<T> r, in T t) { r = default; } // 4
    void F13<T>(out R<T> r, out T t) { r = default; t = default; }
}";
            var comp = CreateCompilation(source, parseOptions: TestOptions.Regular.WithLanguageVersion(langVersion));
            comp.VerifyDiagnostics(
                // (10,10): warning CS9060: Method 'C.F2<T>(ref T)' may capture a ref parameter in a ref struct field.
                //     R<T> F2<T>(ref T t) => throw null; // 1
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F2").WithArguments("C.F2<T>(ref T)").WithLocation(10, 10),
                // (11,10): warning CS9060: Method 'C.F3<T>(in T)' may capture a ref parameter in a ref struct field.
                //     R<T> F3<T>(in T t) => throw null; // 2
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F3").WithArguments("C.F3<T>(in T)").WithLocation(11, 10),
                // (19,10): warning CS9060: Method 'C.F11<T>(out R<T>, ref T)' may capture a ref parameter in a ref struct field.
                //     void F11<T>(out R<T> r, ref T t) { r = default; } // 3
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F11").WithArguments("C.F11<T>(out R<T>, ref T)").WithLocation(19, 10),
                // (20,10): warning CS9060: Method 'C.F12<T>(out R<T>, in T)' may capture a ref parameter in a ref struct field.
                //     void F12<T>(out R<T> r, in T t) { r = default; } // 4
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F12").WithArguments("C.F12<T>(out R<T>, in T)").WithLocation(20, 10));
        }

        [Fact]
        public void UnannotatedMethodWarning_InstanceMethods_02()
        {
            var source =
@"ref struct R<T>
{
#pragma warning disable 649
    internal T F;
}
struct S
{
    R<T> F0<T>() => throw null; // 1
    R<T> F1<T>(T t) => throw null; // 2
    R<T> F2<T>(ref T t) => throw null; // 3
    R<T> F3<T>(in T t) => throw null; // 4
    R<T> F4<T>(out T t) => throw null; // 5
    void F5() { }
    void F6<T>(R<T> r) { }
    void F7<T>(ref R<T> r) { } // 6
    void F8<T>(in R<T> r) { }
    void F9<T>(out R<T> r) { r = default; } // 7
    void F10<T>(out R<T> r, T t) { r = default; } // 8
    void F11<T>(out R<T> r, ref T t) { r = default; } // 9
    void F12<T>(out R<T> r, in T t) { r = default; } // 10
    void F13<T>(out R<T> r, out T t) { r = default; t = default; } // 11
}";
            var comp = CreateCompilation(source);
            comp.VerifyDiagnostics(
                // (8,10): warning CS9060: Method 'S.F0<T>()' may capture a ref parameter in a ref struct field.
                //     R<T> F0<T>() => throw null; // 1
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F0").WithArguments("S.F0<T>()").WithLocation(8, 10),
                // (9,10): warning CS9060: Method 'S.F1<T>(T)' may capture a ref parameter in a ref struct field.
                //     R<T> F1<T>(T t) => throw null; // 2
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F1").WithArguments("S.F1<T>(T)").WithLocation(9, 10),
                // (10,10): warning CS9060: Method 'S.F2<T>(ref T)' may capture a ref parameter in a ref struct field.
                //     R<T> F2<T>(ref T t) => throw null; // 3
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F2").WithArguments("S.F2<T>(ref T)").WithLocation(10, 10),
                // (11,10): warning CS9060: Method 'S.F3<T>(in T)' may capture a ref parameter in a ref struct field.
                //     R<T> F3<T>(in T t) => throw null; // 4
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F3").WithArguments("S.F3<T>(in T)").WithLocation(11, 10),
                // (12,10): warning CS9060: Method 'S.F4<T>(out T)' may capture a ref parameter in a ref struct field.
                //     R<T> F4<T>(out T t) => throw null; // 5
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F4").WithArguments("S.F4<T>(out T)").WithLocation(12, 10),
                // (15,10): warning CS9060: Method 'S.F7<T>(ref R<T>)' may capture a ref parameter in a ref struct field.
                //     void F7<T>(ref R<T> r) { } // 6
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F7").WithArguments("S.F7<T>(ref R<T>)").WithLocation(15, 10),
                // (17,10): warning CS9060: Method 'S.F9<T>(out R<T>)' may capture a ref parameter in a ref struct field.
                //     void F9<T>(out R<T> r) { r = default; } // 7
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F9").WithArguments("S.F9<T>(out R<T>)").WithLocation(17, 10),
                // (18,10): warning CS9060: Method 'S.F10<T>(out R<T>, T)' may capture a ref parameter in a ref struct field.
                //     void F10<T>(out R<T> r, T t) { r = default; } // 8
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F10").WithArguments("S.F10<T>(out R<T>, T)").WithLocation(18, 10),
                // (19,10): warning CS9060: Method 'S.F11<T>(out R<T>, ref T)' may capture a ref parameter in a ref struct field.
                //     void F11<T>(out R<T> r, ref T t) { r = default; } // 9
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F11").WithArguments("S.F11<T>(out R<T>, ref T)").WithLocation(19, 10),
                // (20,10): warning CS9060: Method 'S.F12<T>(out R<T>, in T)' may capture a ref parameter in a ref struct field.
                //     void F12<T>(out R<T> r, in T t) { r = default; } // 10
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F12").WithArguments("S.F12<T>(out R<T>, in T)").WithLocation(20, 10),
                // (21,10): warning CS9060: Method 'S.F13<T>(out R<T>, out T)' may capture a ref parameter in a ref struct field.
                //     void F13<T>(out R<T> r, out T t) { r = default; t = default; } // 11
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F13").WithArguments("S.F13<T>(out R<T>, out T)").WithLocation(21, 10));
        }

        [Fact]
        public void UnannotatedMethodWarning_InstanceMethods_03()
        {
            var source =
@"ref struct R0<T>
{
#pragma warning disable 649
    internal T F;
}
ref struct R1
{
    R0<T> F0<T>() => throw null; // 1
    R0<T> F1<T>(T t) => throw null; // 2
    R0<T> F2<T>(ref T t) => throw null; // 3
    R0<T> F3<T>(in T t) => throw null; // 4
    R0<T> F4<T>(out T t) => throw null; // 5
    void F5() { }
    void F6<T>(R0<T> r) { }
    void F7<T>(ref R0<T> r) { } // 6
    void F8<T>(in R0<T> r) { }
    void F9<T>(out R0<T> r) { r = default; } // 7
    void F10<T>(out R0<T> r, T t) { r = default; } // 8
    void F11<T>(out R0<T> r, ref T t) { r = default; } // 9
    void F12<T>(out R0<T> r, in T t) { r = default; } // 10
    void F13<T>(out R0<T> r, out T t) { r = default; t = default; } // 11
}";
            var comp = CreateCompilation(source);
            comp.VerifyDiagnostics(
                // (8,11): warning CS9060: Method 'R1.F0<T>()' may capture a ref parameter in a ref struct field.
                //     R0<T> F0<T>() => throw null; // 1
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F0").WithArguments("R1.F0<T>()").WithLocation(8, 11),
                // (9,11): warning CS9060: Method 'R1.F1<T>(T)' may capture a ref parameter in a ref struct field.
                //     R0<T> F1<T>(T t) => throw null; // 2
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F1").WithArguments("R1.F1<T>(T)").WithLocation(9, 11),
                // (10,11): warning CS9060: Method 'R1.F2<T>(ref T)' may capture a ref parameter in a ref struct field.
                //     R0<T> F2<T>(ref T t) => throw null; // 3
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F2").WithArguments("R1.F2<T>(ref T)").WithLocation(10, 11),
                // (11,11): warning CS9060: Method 'R1.F3<T>(in T)' may capture a ref parameter in a ref struct field.
                //     R0<T> F3<T>(in T t) => throw null; // 4
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F3").WithArguments("R1.F3<T>(in T)").WithLocation(11, 11),
                // (12,11): warning CS9060: Method 'R1.F4<T>(out T)' may capture a ref parameter in a ref struct field.
                //     R0<T> F4<T>(out T t) => throw null; // 5
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F4").WithArguments("R1.F4<T>(out T)").WithLocation(12, 11),
                // (15,10): warning CS9060: Method 'R1.F7<T>(ref R0<T>)' may capture a ref parameter in a ref struct field.
                //     void F7<T>(ref R0<T> r) { } // 6
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F7").WithArguments("R1.F7<T>(ref R0<T>)").WithLocation(15, 10),
                // (16,10): warning CS9060: Method 'R1.F8<T>(in R0<T>)' may capture a ref parameter in a ref struct field.
                //     void F8<T>(in R0<T> r) { }
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F8").WithArguments("R1.F8<T>(in R0<T>)").WithLocation(16, 10),
                // (17,10): warning CS9060: Method 'R1.F9<T>(out R0<T>)' may capture a ref parameter in a ref struct field.
                //     void F9<T>(out R0<T> r) { r = default; } // 7
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F9").WithArguments("R1.F9<T>(out R0<T>)").WithLocation(17, 10),
                // (18,10): warning CS9060: Method 'R1.F10<T>(out R0<T>, T)' may capture a ref parameter in a ref struct field.
                //     void F10<T>(out R0<T> r, T t) { r = default; } // 8
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F10").WithArguments("R1.F10<T>(out R0<T>, T)").WithLocation(18, 10),
                // (19,10): warning CS9060: Method 'R1.F11<T>(out R0<T>, ref T)' may capture a ref parameter in a ref struct field.
                //     void F11<T>(out R0<T> r, ref T t) { r = default; } // 9
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F11").WithArguments("R1.F11<T>(out R0<T>, ref T)").WithLocation(19, 10),
                // (20,10): warning CS9060: Method 'R1.F12<T>(out R0<T>, in T)' may capture a ref parameter in a ref struct field.
                //     void F12<T>(out R0<T> r, in T t) { r = default; } // 10
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F12").WithArguments("R1.F12<T>(out R0<T>, in T)").WithLocation(20, 10),
                // (21,10): warning CS9060: Method 'R1.F13<T>(out R0<T>, out T)' may capture a ref parameter in a ref struct field.
                //     void F13<T>(out R0<T> r, out T t) { r = default; t = default; } // 11
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F13").WithArguments("R1.F13<T>(out R0<T>, out T)").WithLocation(21, 10));
        }

        [Fact]
        public void UnannotatedMethodWarning_Constructors()
        {
            var source =
@"struct S<T>
{
    public S() { }
    S(S<byte> b) { }
    S(ref S<int> i) { }
    S(in S<object> o) { }
    S(out S<string> s) { s = default; }
    S(R<byte> r) { }
    S(ref R<int> r) { } // 1
    S(in R<object> r) { }
    S(out R<string> r) { r = default; } // 2
}
ref struct R<T>
{
    public R() { }
    R(S<byte> b) { }
    R(ref S<int> i) { } // 3
    R(in S<object> o) { } // 4
    R(out S<string> s) { s = default; }
    R(R<byte> r) { }
    R(ref R<int> r) { } // 5
    R(in R<object> r) { } // 6
    R(out R<string> r) { r = default; } // 7
}";
            var comp = CreateCompilation(source);
            comp.VerifyDiagnostics(
                // (9,5): warning CS9060: Method 'S<T>.S(ref R<int>)' may capture a ref parameter in a ref struct field.
                //     S(ref R<int> r) { } // 1
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "S").WithArguments("S<T>.S(ref R<int>)").WithLocation(9, 5),
                // (11,5): warning CS9060: Method 'S<T>.S(out R<string>)' may capture a ref parameter in a ref struct field.
                //     S(out R<string> r) { r = default; } // 2
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "S").WithArguments("S<T>.S(out R<string>)").WithLocation(11, 5),
                // (17,5): warning CS9060: Method 'R<T>.R(ref S<int>)' may capture a ref parameter in a ref struct field.
                //     R(ref S<int> i) { } // 3
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "R").WithArguments("R<T>.R(ref S<int>)").WithLocation(17, 5),
                // (18,5): warning CS9060: Method 'R<T>.R(in S<object>)' may capture a ref parameter in a ref struct field.
                //     R(in S<object> o) { } // 4
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "R").WithArguments("R<T>.R(in S<object>)").WithLocation(18, 5),
                // (21,5): warning CS9060: Method 'R<T>.R(ref R<int>)' may capture a ref parameter in a ref struct field.
                //     R(ref R<int> r) { } // 5
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "R").WithArguments("R<T>.R(ref R<int>)").WithLocation(21, 5),
                // (22,5): warning CS9060: Method 'R<T>.R(in R<object>)' may capture a ref parameter in a ref struct field.
                //     R(in R<object> r) { } // 6
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "R").WithArguments("R<T>.R(in R<object>)").WithLocation(22, 5),
                // (23,5): warning CS9060: Method 'R<T>.R(out R<string>)' may capture a ref parameter in a ref struct field.
                //     R(out R<string> r) { r = default; } // 7
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "R").WithArguments("R<T>.R(out R<string>)").WithLocation(23, 5));
        }

        [Fact]
        public void UnannotatedMethodWarning_InterfaceMethods()
        {
            var source =
@"ref struct R<T> { }
interface I<T>
{
    R<T> F0();
    R<T> F1(T t);
    R<T> F2(ref T t); // 1
    R<T> F3(in T t); // 2
    R<T> F4(out T t);
    void F5(out R<T> r);
    void F6(out R<T> r, T t);
    void F7(out R<T> r, ref T t); // 3
    void F8(out R<T> r, in T t); // 4
    void F9(out R<T> r, out T t);
}
class C<T> : I<T>
{
    R<T> I<T>.F0() => throw null;
    R<T> I<T>.F1(T t) => throw null;
    R<T> I<T>.F2(ref T t) => throw null; // 5
    R<T> I<T>.F3(in T t) => throw null; // 6
    R<T> I<T>.F4(out T t) => throw null;
    void I<T>.F5(out R<T> r) => throw null;
    void I<T>.F6(out R<T> r, T t) => throw null;
    void I<T>.F7(out R<T> r, ref T t) => throw null; // 7
    void I<T>.F8(out R<T> r, in T t) => throw null; // 8
    void I<T>.F9(out R<T> r, out T t) => throw null;
}";
            var comp = CreateCompilation(source);
            comp.VerifyDiagnostics(
                // (6,10): warning CS9060: Method 'I<T>.F2(ref T)' may capture a ref parameter in a ref struct field.
                //     R<T> F2(ref T t); // 1
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F2").WithArguments("I<T>.F2(ref T)").WithLocation(6, 10),
                // (7,10): warning CS9060: Method 'I<T>.F3(in T)' may capture a ref parameter in a ref struct field.
                //     R<T> F3(in T t); // 2
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F3").WithArguments("I<T>.F3(in T)").WithLocation(7, 10),
                // (11,10): warning CS9060: Method 'I<T>.F7(out R<T>, ref T)' may capture a ref parameter in a ref struct field.
                //     void F7(out R<T> r, ref T t); // 3
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F7").WithArguments("I<T>.F7(out R<T>, ref T)").WithLocation(11, 10),
                // (12,10): warning CS9060: Method 'I<T>.F8(out R<T>, in T)' may capture a ref parameter in a ref struct field.
                //     void F8(out R<T> r, in T t); // 4
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F8").WithArguments("I<T>.F8(out R<T>, in T)").WithLocation(12, 10),
                // (19,15): warning CS9060: Method 'C<T>.I<T>.F2(ref T)' may capture a ref parameter in a ref struct field.
                //     R<T> I<T>.F2(ref T t) => throw null; // 5
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F2").WithArguments("C<T>.I<T>.F2(ref T)").WithLocation(19, 15),
                // (20,15): warning CS9060: Method 'C<T>.I<T>.F3(in T)' may capture a ref parameter in a ref struct field.
                //     R<T> I<T>.F3(in T t) => throw null; // 6
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F3").WithArguments("C<T>.I<T>.F3(in T)").WithLocation(20, 15),
                // (24,15): warning CS9060: Method 'C<T>.I<T>.F7(out R<T>, ref T)' may capture a ref parameter in a ref struct field.
                //     void I<T>.F7(out R<T> r, ref T t) => throw null; // 7
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F7").WithArguments("C<T>.I<T>.F7(out R<T>, ref T)").WithLocation(24, 15),
                // (25,15): warning CS9060: Method 'C<T>.I<T>.F8(out R<T>, in T)' may capture a ref parameter in a ref struct field.
                //     void I<T>.F8(out R<T> r, in T t) => throw null; // 8
                Diagnostic(ErrorCode.WRN_MayCaptureRefField, "F8").WithArguments("C<T>.I<T>.F8(out R<T>, in T)").WithLocation(25, 15));
        }

        // PROTOTYPE: Test:
        // - properties with various accessors
        // - partial method with/without implementation
        // - unsafe has no affect
        // - operators
        // - local functions
        // - lambdas
        // - delegate types
    }
}
