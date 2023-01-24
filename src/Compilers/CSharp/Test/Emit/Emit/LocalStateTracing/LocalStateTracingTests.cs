// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public class LocalStateTracingTests : CSharpTestBase
    {
        // TODO:
        // test that we don't instrument:
        // - field/property assignment
        // lifted local assignment (SM, lambda)
        // assignment to this (parameter)
        // pointer, fptr locals
        // Dict.TryGetValue(x, out var z); - where Dict is a library

        private static readonly EmitOptions s_emitOptions = EmitOptions.Default
            .WithInstrumentationKinds(ImmutableArray.Create(InstrumentationKind.LocalStateTracing));

        private static readonly CSharpCompilationOptions s_options = TestOptions.UnsafeDebugDll;

        private const string TrackerTypeName = "Microsoft.CodeAnalysis.Runtime.LocalStoreTracker";

        private static readonly string s_helpers = @"
namespace Microsoft.CodeAnalysis.Runtime
{
    public unsafe readonly struct LocalStoreTracker
    {
        public static LocalStoreTracker LogMethodEntry(int methodId) => new();
        public static LocalStoreTracker LogMethodEntry(int methodId, int addressCount) => new();

        public void LogLocalLoadAddress<T>(ref T local, ushort index) {}
        public void LogParameterLoadAddress<T>(ref T param, ushort index) {}

        public void LogLocalStore(bool value, ushort index) {}
        public void LogLocalStore(byte value, ushort index) {}
        public void LogLocalStore(ushort value, ushort index) {}
        public void LogLocalStore(uint value, ushort index) {}
        public void LogLocalStore(ulong value, ushort index) {}
        public void LogLocalStore(string value, ushort index) {}
        public void LogLocalStore(object value, ushort index) {}
        public void LogLocalStoreUnmanaged<T>(ref T local, ushort index) where T : unmanaged {}

        public void LogParameterStore(bool value, ushort index) {}
        public void LogParameterStore(byte value, ushort index) {}
        public void LogParameterStore(ushort value, ushort index) {}
        public void LogParameterStore(uint value, ushort index) {}
        public void LogParameterStore(ulong value, ushort index) {}
        public void LogParameterStore(string value, ushort index) {}
        public void LogParameterStore(object value, ushort index) {}
        public void LogParameterStoreUnmanaged<T>(ref T local, ushort index) where T : unmanaged {}

        public void LogLocalStoreAddress<T>(ref T local, ushort index) {}
        public void LogParameterStoreAddress<T>(ref T param, ushort index) {}
    }
}
";
        private static string WithHelpers(string source)
            => source + s_helpers;

        private static readonly Verification s_verification = Verification.Passes;

        private CompilationVerifier CompileAndVerify(string source, Verification? verify = null)
            => CompileAndVerify(source, options: s_options, emitOptions: s_emitOptions, verify: verify ?? s_verification, targetFramework: TargetFramework.Net70);

        private static void AssertNotInstrumented(CompilationVerifier verifier, string qualifiedMethodName)
            => AssertInstrumented(verifier, qualifiedMethodName, expected: false);

        private static void AssertInstrumented(CompilationVerifier verifier, string qualifiedMethodName, bool expected = true)
        {
            var il = verifier.VisualizeIL(qualifiedMethodName);
            var isInstrumented = il.Contains(TrackerTypeName);

            AssertEx.AreEqual(expected, isInstrumented,
                $"Method '{qualifiedMethodName}' should {(expected ? "be" : "not be")} instrumented. Actual IL:{Environment.NewLine}{il}");
        }

        [Fact]
        public void HelpersNotInstrumented()
        {
            var source = WithHelpers("");
            var verifier = CompileAndVerify(source);
            foreach (var entry in verifier.TestData.Methods)
            {
                string actualIL = verifier.VisualizeIL(entry.Value, realIL: false);
                Assert.False(actualIL.Contains(TrackerTypeName + ".Log"));
            }
        }

        [Fact]
        public void ObjectInitializers_NotInstrumented()
        {
            var source = WithHelpers(@"
class C
{
    int X;

    public C F() => new C() { X = 1 };
}
");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", @"
{
  // Code size       24 (0x18)
  .maxstack  3
  .locals init (Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_0)
  // sequence point: => new C() { X = 1 }
  IL_0000:  ldtoken    ""C C.F()""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.0
  // sequence point: new C() { X = 1 }
  IL_000b:  newobj     ""C..ctor()""
  IL_0010:  dup
  IL_0011:  ldc.i4.1
  IL_0012:  stfld      ""int C.X""
  IL_0017:  ret
}
", sequencePoints: "C.F", source: source);
        }

        [Fact]
        public void WithExpressions_NotInstrumented()
        {
            var source = WithHelpers(@"
record class C(int X)
{
    public C F() => new C(0) with { X = 1 };
}
");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", @"
{
  // Code size       31 (0x1f)
  .maxstack  3
  .locals init (Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_0)
  // sequence point: => new C(0) with { X = 1 }
  IL_0000:  ldtoken    ""C C.F()""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.0
  // sequence point: new C(0) with { X = 1 }
  IL_000b:  ldc.i4.0
  IL_000c:  newobj     ""C..ctor(int)""
  IL_0011:  callvirt   ""C C.<Clone>$()""
  IL_0016:  dup
  IL_0017:  ldc.i4.1
  IL_0018:  callvirt   ""void C.X.init""
  IL_001d:  nop
  IL_001e:  ret
}
", sequencePoints: "C.F", source: source);
        }

        [Fact]
        public void MemberAssignment_NotInstrumented()
        {
            var source = WithHelpers(@"
class C
{
    int X;
    int P { get => 1; set { X = value; } }

    public void F() { X = 1; P = 2; }
}
");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", @"
    {
  // Code size       27 (0x1b)
  .maxstack  2
  .locals init (Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_0)
  // sequence point: {
  IL_0000:  ldtoken    ""void C.F()""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.0
  // sequence point: X = 1;
  IL_000b:  ldarg.0
  IL_000c:  ldc.i4.1
  IL_000d:  stfld      ""int C.X""
  // sequence point: P = 2;
  IL_0012:  ldarg.0
  IL_0013:  ldc.i4.2
  IL_0014:  call       ""void C.P.set""
  IL_0019:  nop
  // sequence point: }
  IL_001a:  ret
}
", sequencePoints: "C.F", source: source);
        }

        [Fact]
        public void AutoPropertyAccessors_NotInstrumented()
        {
            var source = WithHelpers(@"
class C
{
    int P { get; set; }
}
");
            var verifier = CompileAndVerify(source);

            AssertNotInstrumented(verifier, "C.P.get");
            AssertNotInstrumented(verifier, "C.P.set");
        }

        [Fact]
        public void EventAccessors_NotInstrumented()
        {
            var source = WithHelpers(@"
using System;

class C
{
    event Action E;
}
");

            var verifier = CompileAndVerify(source);

            AssertNotInstrumented(verifier, "C.P.add");
            AssertNotInstrumented(verifier, "C.P.remove");
        }

        [Fact]
        public void SimpleLocalsAndParameters()
        {
            var source = WithHelpers(@"
class C
{
    public void F(int p, int q)
    {
        int x = 1;
        p = x = 2;
        for (int i = 0; i < 10; i++)
        {
            q = x += 2;
        }
    }
}
");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", @"
{
  // Code size      139 (0x8b)
  .maxstack  4
  .locals init (int V_0, //x
                Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_1,
                int V_2, //i
                int V_3,
                bool V_4)
  // sequence point: {
  IL_0000:  ldtoken    ""void C.F(int, int)""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.1
  IL_000b:  ldloca.s   V_1
  IL_000d:  ldarg.1
  IL_000e:  ldc.i4.0
  IL_000f:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(uint, ushort)""
  IL_0014:  nop
  IL_0015:  ldloca.s   V_1
  IL_0017:  ldarg.2
  IL_0018:  ldc.i4.1
  IL_0019:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(uint, ushort)""
  IL_001e:  nop
  // sequence point: int x = 1;
  IL_001f:  ldloca.s   V_1
  IL_0021:  ldc.i4.1
  IL_0022:  dup
  IL_0023:  stloc.0
  IL_0024:  ldc.i4.0
  IL_0025:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_002a:  nop
  // sequence point: p = x = 2;
  IL_002b:  ldloca.s   V_1
  IL_002d:  ldloca.s   V_1
  IL_002f:  ldc.i4.2
  IL_0030:  dup
  IL_0031:  stloc.0
  IL_0032:  ldc.i4.0
  IL_0033:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_0038:  nop
  IL_0039:  ldloc.0
  IL_003a:  dup
  IL_003b:  starg.s    V_1
  IL_003d:  ldc.i4.0
  IL_003e:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(uint, ushort)""
  IL_0043:  nop
  // sequence point: int i = 0
  IL_0044:  ldloca.s   V_1
  IL_0046:  ldc.i4.0
  IL_0047:  dup
  IL_0048:  stloc.2
  IL_0049:  ldc.i4.2
  IL_004a:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_004f:  nop
  // sequence point: <hidden>
  IL_0050:  br.s       IL_007f
  // sequence point: {
  IL_0052:  nop
  // sequence point: q = x += 2;
  IL_0053:  ldloca.s   V_1
  IL_0055:  ldloca.s   V_1
  IL_0057:  ldloc.0
  IL_0058:  ldc.i4.2
  IL_0059:  add
  IL_005a:  dup
  IL_005b:  stloc.0
  IL_005c:  ldc.i4.0
  IL_005d:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_0062:  nop
  IL_0063:  ldloc.0
  IL_0064:  dup
  IL_0065:  starg.s    V_2
  IL_0067:  ldc.i4.1
  IL_0068:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(uint, ushort)""
  IL_006d:  nop
  // sequence point: }
  IL_006e:  nop
  // sequence point: i++
  IL_006f:  ldloc.2
  IL_0070:  stloc.3
  IL_0071:  ldloca.s   V_1
  IL_0073:  ldloc.3
  IL_0074:  ldc.i4.1
  IL_0075:  add
  IL_0076:  dup
  IL_0077:  stloc.2
  IL_0078:  ldc.i4.2
  IL_0079:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_007e:  nop
  // sequence point: i < 10
  IL_007f:  ldloc.2
  IL_0080:  ldc.i4.s   10
  IL_0082:  clt
  IL_0084:  stloc.s    V_4
  // sequence point: <hidden>
  IL_0086:  ldloc.s    V_4
  IL_0088:  brtrue.s   IL_0052
  // sequence point: }
  IL_008a:  ret
}
", sequencePoints: "C.F", source: source);
        }

        [Theory]
        [InlineData("I", "object")]
        [InlineData("C", "object")]
        [InlineData("object", "object")]
        [InlineData("string", "string")]
        [InlineData("bool", "bool")]
        [InlineData("byte", "byte")]
        [InlineData("ushort", "ushort")]
        [InlineData("char", "ushort")]
        [InlineData("int", "uint")]
        [InlineData("uint", "uint")]
        [InlineData("long", "ulong")]
        [InlineData("ulong", "ulong")]
        public void SpecialTypes_NoConv(string typeName, string targetType)
        {
            var source = WithHelpers($$"""
interface I {}

class C
{
    private static readonly {{typeName}} s;

    public void F({{typeName}} p)
    {
        var x = p = s;
    }
}
""");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", $@"
{{
  // Code size       51 (0x33)
  .maxstack  4
  .locals init ({typeName} V_0, //x
                Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_1)
  // sequence point: {{
  IL_0000:  ldtoken    ""void C.F({typeName})""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.1
  IL_000b:  ldloca.s   V_1
  IL_000d:  ldarg.1
  IL_000e:  ldc.i4.0
  IL_000f:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore({targetType}, ushort)""
  IL_0014:  nop
  // sequence point: var x = p = s;
  IL_0015:  ldloca.s   V_1
  IL_0017:  ldloca.s   V_1
  IL_0019:  ldsfld     ""{typeName} C.s""
  IL_001e:  dup
  IL_001f:  starg.s    V_1
  IL_0021:  ldc.i4.0
  IL_0022:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore({targetType}, ushort)""
  IL_0027:  nop
  IL_0028:  ldarg.1
  IL_0029:  dup
  IL_002a:  stloc.0
  IL_002b:  ldc.i4.0
  IL_002c:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore({targetType}, ushort)""
  IL_0031:  nop
  // sequence point: }}
  IL_0032:  ret
}}
", sequencePoints: "C.F", source: source);
        }

        [Theory]
        [InlineData("sbyte", "byte", "conv.u1")]
        [InlineData("short", "ushort", "conv.u2")]
        [InlineData("float", "uint", "conv.u4")]
        [InlineData("double", "ulong", "conv.u8")]
        public void SpecialTypes_ConvU(string typeName, string targetType, string conversion)
        {
            var source = WithHelpers($$"""
class C
{
    private static readonly {{typeName}} s;

    public void F({{typeName}} p)
    {
        var x = p = s;
    }
}
""");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", $@"
{{
  // Code size       54 (0x36)
  .maxstack  4
  .locals init ({typeName} V_0, //x
                Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_1)
  // sequence point: {{
  IL_0000:  ldtoken    ""void C.F({typeName})""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.1
  IL_000b:  ldloca.s   V_1
  IL_000d:  ldarg.1
  IL_000e:  {conversion}
  IL_000f:  ldc.i4.0
  IL_0010:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore({targetType}, ushort)""
  IL_0015:  nop
  // sequence point: var x = p = s;
  IL_0016:  ldloca.s   V_1
  IL_0018:  ldloca.s   V_1
  IL_001a:  ldsfld     ""{typeName} C.s""
  IL_001f:  dup
  IL_0020:  starg.s    V_1
  IL_0022:  {conversion}
  IL_0023:  ldc.i4.0
  IL_0024:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore({targetType}, ushort)""
  IL_0029:  nop
  IL_002a:  ldarg.1
  IL_002b:  dup
  IL_002c:  stloc.0
  IL_002d:  {conversion}
  IL_002e:  ldc.i4.0
  IL_002f:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore({targetType}, ushort)""
  IL_0034:  nop
  // sequence point: }}
  IL_0035:  ret
}}
", sequencePoints: "C.F", source: source);
        }

        [Theory]
        [InlineData("byte", "byte")]
        [InlineData("ushort", "ushort")]
        [InlineData("int", "uint")]
        [InlineData("uint", "uint")]
        [InlineData("long", "ulong")]
        [InlineData("ulong", "ulong")]
        public void Enums_NoConv(string typeName, string targetType)
        {
            var source = WithHelpers($$"""
enum E : {{typeName}}
{
}

class C
{
    private static readonly E s;

    public void F(E p)
    {
        var x = p = s;
    }
}
""");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", $@"
{{
  // Code size       51 (0x33)
  .maxstack  4
  .locals init (E V_0, //x
                Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_1)
  // sequence point: {{
  IL_0000:  ldtoken    ""void C.F(E)""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.1
  IL_000b:  ldloca.s   V_1
  IL_000d:  ldarg.1
  IL_000e:  ldc.i4.0
  IL_000f:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore({targetType}, ushort)""
  IL_0014:  nop
  // sequence point: var x = p = s;
  IL_0015:  ldloca.s   V_1
  IL_0017:  ldloca.s   V_1
  IL_0019:  ldsfld     ""E C.s""
  IL_001e:  dup
  IL_001f:  starg.s    V_1
  IL_0021:  ldc.i4.0
  IL_0022:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore({targetType}, ushort)""
  IL_0027:  nop
  IL_0028:  ldarg.1
  IL_0029:  dup
  IL_002a:  stloc.0
  IL_002b:  ldc.i4.0
  IL_002c:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore({targetType}, ushort)""
  IL_0031:  nop
  // sequence point: }}
  IL_0032:  ret
}}
", sequencePoints: "C.F", source: source);
        }

        [Theory]
        [InlineData("sbyte", "byte", "conv.u1")]
        [InlineData("short", "ushort", "conv.u2")]
        public void Enums_ConvU(string typeName, string targetType, string conversion)
        {
            var source = WithHelpers($$"""
enum E : {{typeName}}
{
}

class C
{
    private static readonly E s;

    public void F(E p)
    {
        var x = p = s;
    }
}
""");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", $@"
{{
  // Code size       54 (0x36)
  .maxstack  4
  .locals init (E V_0, //x
                Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_1)
  // sequence point: {{
  IL_0000:  ldtoken    ""void C.F(E)""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.1
  IL_000b:  ldloca.s   V_1
  IL_000d:  ldarg.1
  IL_000e:  {conversion}
  IL_000f:  ldc.i4.0
  IL_0010:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore({targetType}, ushort)""
  IL_0015:  nop
  // sequence point: var x = p = s;
  IL_0016:  ldloca.s   V_1
  IL_0018:  ldloca.s   V_1
  IL_001a:  ldsfld     ""E C.s""
  IL_001f:  dup
  IL_0020:  starg.s    V_1
  IL_0022:  {conversion}
  IL_0023:  ldc.i4.0
  IL_0024:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore({targetType}, ushort)""
  IL_0029:  nop
  IL_002a:  ldarg.1
  IL_002b:  dup
  IL_002c:  stloc.0
  IL_002d:  {conversion}
  IL_002e:  ldc.i4.0
  IL_002f:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore({targetType}, ushort)""
  IL_0034:  nop
  // sequence point: }}
  IL_0035:  ret
}}
", sequencePoints: "C.F", source: source);
        }

        [Theory]
        [InlineData("", "decimal")]
        [InlineData("", "System.DateTime")]
        [InlineData("", "System.Guid")]
        [InlineData("", "System.ValueTuple<int, bool>")]
        [InlineData("struct S { }", "S")]
        [InlineData("struct S<T> where T : struct { T t; }", "S<int>")]
        [InlineData("struct S { System.DateTime X; System.Guid Y; decimal Z; unsafe void* P; }", "S")]
        public void UnmanagedStruct(string definition, string typeName)
        {
            var source = WithHelpers(definition + $$"""
class C
{
    private static readonly {{typeName}} s;

    public void F({{typeName}} p)
    {
        var x = p = s;
    }
}
""");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", $@"
{{
  // Code size       54 (0x36)
  .maxstack  4
  .locals init ({typeName} V_0, //x
                Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_1)
  // sequence point: {{
  IL_0000:  ldtoken    ""void C.F({typeName})""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.1
  IL_000b:  ldloca.s   V_1
  IL_000d:  ldarga.s   V_1
  IL_000f:  ldc.i4.0
  IL_0010:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStoreUnmanaged<{typeName}>(ref {typeName}, ushort)""
  IL_0015:  nop
  // sequence point: var x = p = s;
  IL_0016:  ldloca.s   V_1
  IL_0018:  ldloca.s   V_1
  IL_001a:  ldsfld     ""{typeName} C.s""
  IL_001f:  starg.s    V_1
  IL_0021:  ldarga.s   V_1
  IL_0023:  ldc.i4.0
  IL_0024:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStoreUnmanaged<{typeName}>(ref {typeName}, ushort)""
  IL_0029:  nop
  IL_002a:  ldarg.1
  IL_002b:  stloc.0
  IL_002c:  ldloca.s   V_0
  IL_002e:  ldc.i4.0
  IL_002f:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStoreUnmanaged<{typeName}>(ref {typeName}, ushort)""
  IL_0034:  nop
  // sequence point: }}
  IL_0035:  ret
}}", sequencePoints: "C.F", source: source);
        }

        [Theory]
        [InlineData("", "System.ValueTuple<int, string>")]
        [InlineData("struct S { string A; }", "S")]
        [InlineData("", "int?")] // TODO: why does Nullable<T> not satisfy unmanaged constraint for unmanaged Ts?
        public void ManagedStruct(string definition, string typeName)
        {
            var source = WithHelpers(definition + $$"""
class C
{
    private static readonly {{typeName}} s;

    public void F({{typeName}} p)
    {
        var x = p = s;
    }
}
""");
            var verifier = CompileAndVerify(source);

            // TODO: why is the stloc+ldloc.a of V_2 emitted?
            // IL_0045: stloc.2
            // IL_0046: ldloca.s V_2

            verifier.VerifyIL("C.F", $@"
{{
  // Code size       91 (0x5b)
  .maxstack  4
  .locals init ({typeName} V_0, //x
                Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_1,
                {typeName} V_2)
  // sequence point: {{
  IL_0000:  ldtoken    ""void C.F({typeName})""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.1
  IL_000b:  ldloca.s   V_1
  IL_000d:  ldarga.s   V_1
  IL_000f:  constrained. ""{typeName}""
  IL_0015:  callvirt   ""string object.ToString()""
  IL_001a:  ldc.i4.0
  IL_001b:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(string, ushort)""
  IL_0020:  nop
  // sequence point: var x = p = s;
  IL_0021:  ldloca.s   V_1
  IL_0023:  ldloca.s   V_1
  IL_0025:  ldsfld     ""{typeName} C.s""
  IL_002a:  dup
  IL_002b:  starg.s    V_1
  IL_002d:  stloc.2
  IL_002e:  ldloca.s   V_2
  IL_0030:  constrained. ""{typeName}""
  IL_0036:  callvirt   ""string object.ToString()""
  IL_003b:  ldc.i4.0
  IL_003c:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(string, ushort)""
  IL_0041:  nop
  IL_0042:  ldarg.1
  IL_0043:  dup
  IL_0044:  stloc.0
  IL_0045:  stloc.2
  IL_0046:  ldloca.s   V_2
  IL_0048:  constrained. ""{typeName}""
  IL_004e:  callvirt   ""string object.ToString()""
  IL_0053:  ldc.i4.0
  IL_0054:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(string, ushort)""
  IL_0059:  nop
  // sequence point: }}
  IL_005a:  ret
}}", sequencePoints: "C.F", source: source);
        }

        [Fact]
        public void RefStructWithRefField()
        {
            var source = WithHelpers($$"""
ref struct S { ref int X; }

class C
{
    public void F(S p)
    {
        var x = p = new S();
    }
}
""");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", @"
{
  // Code size       92 (0x5c)
  .maxstack  4
  .locals init (S V_0, //x
                Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_1,
                S V_2)
  // sequence point: {
  IL_0000:  ldtoken    ""void C.F(S)""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.1
  IL_000b:  ldloca.s   V_1
  IL_000d:  ldarga.s   V_1
  IL_000f:  constrained. ""S""
  IL_0015:  callvirt   ""string object.ToString()""
  IL_001a:  ldc.i4.0
  IL_001b:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(string, ushort)""
  IL_0020:  nop
  // sequence point: var x = p = new S();
  IL_0021:  ldloca.s   V_1
  IL_0023:  ldloca.s   V_1
  IL_0025:  ldarga.s   V_1
  IL_0027:  initobj    ""S""
  IL_002d:  ldarg.1
  IL_002e:  stloc.2
  IL_002f:  ldloca.s   V_2
  IL_0031:  constrained. ""S""
  IL_0037:  callvirt   ""string object.ToString()""
  IL_003c:  ldc.i4.0
  IL_003d:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(string, ushort)""
  IL_0042:  nop
  IL_0043:  ldarg.1
  IL_0044:  dup
  IL_0045:  stloc.0
  IL_0046:  stloc.2
  IL_0047:  ldloca.s   V_2
  IL_0049:  constrained. ""S""
  IL_004f:  callvirt   ""string object.ToString()""
  IL_0054:  ldc.i4.0
  IL_0055:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(string, ushort)""
  IL_005a:  nop
  // sequence point: }
  IL_005b:  ret
}
", sequencePoints: "C.F", source: source);
        }

        [Fact]
        public void UnmanagedRefStruct()
        {
            var source = WithHelpers($$"""
ref struct S { int X; }

class C
{
    public void F(S p)
    {
        var x = p = new S();
    }
}
""");
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("C.F", $@"
{{
  // Code size       55 (0x37)
  .maxstack  4
  .locals init (S V_0, //x
                Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_1)
  // sequence point: {{
  IL_0000:  ldtoken    ""void C.F(S)""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.1
  IL_000b:  ldloca.s   V_1
  IL_000d:  ldarga.s   V_1
  IL_000f:  ldc.i4.0
  IL_0010:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStoreUnmanaged<S>(ref S, ushort)""
  IL_0015:  nop
  // sequence point: var x = p = new S();
  IL_0016:  ldloca.s   V_1
  IL_0018:  ldloca.s   V_1
  IL_001a:  ldarga.s   V_1
  IL_001c:  initobj    ""S""
  IL_0022:  ldarga.s   V_1
  IL_0024:  ldc.i4.0
  IL_0025:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStoreUnmanaged<S>(ref S, ushort)""
  IL_002a:  nop
  IL_002b:  ldarg.1
  IL_002c:  stloc.0
  IL_002d:  ldloca.s   V_0
  IL_002f:  ldc.i4.0
  IL_0030:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStoreUnmanaged<S>(ref S, ushort)""
  IL_0035:  nop
  // sequence point: }}
  IL_0036:  ret
}}", sequencePoints: "C.F", source: source);
        }

        [Fact]
        public void RefAssignments()
        {
            var source = WithHelpers(@"
class C
{
    public void G(int p1, ref int p2, out int p3)
    {
        int a = 1;
        int b = 2;
        p3 = 3;

        ref int r1 = ref a;
        ref int r2 = ref b, r3 = ref p1, r4 = ref p2, r5 = ref p3, r6 = ref r1;
        if (F(ref r1, ref r2, ref r3, ref r4, ref r5, out r6))
        {
            r1 = r2;
        }
    }

    public bool F(ref int a1, ref int a2, ref int a3, ref int a4, ref int a5, out int a6)
    {
        a6 = 0;
        return true;
    }
}
");
            var verifier = CompileAndVerify(source);

            // TODO: eliminate
            // IL_0067: ldloc.3
            // IL_0068: ldind.i4
            // IL_0069:  pop

            verifier.VerifyIL("C.G", @"
{
  // Code size      230 (0xe6)
  .maxstack  7
  .locals init (int V_0, //a
                int V_1, //b
                int& V_2, //r1
                int& V_3, //r2
                int& V_4, //r3
                int& V_5, //r4
                int& V_6, //r5
                int& V_7, //r6
                Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_8,
                int V_9,
                bool V_10)
  // sequence point: {
  IL_0000:  ldtoken    ""void C.G(int, ref int, out int)""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.s    V_8
  IL_000c:  ldloca.s   V_8
  IL_000e:  ldarg.1
  IL_000f:  ldc.i4.0
  IL_0010:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(uint, ushort)""
  IL_0015:  nop
  IL_0016:  ldloca.s   V_8
  IL_0018:  ldarg.2
  IL_0019:  ldc.i4.1
  IL_001a:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStoreAddress<int>(ref int, ushort)""
  IL_001f:  nop
  IL_0020:  ldloca.s   V_8
  IL_0022:  ldarg.3
  IL_0023:  ldc.i4.2
  IL_0024:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStoreAddress<int>(ref int, ushort)""
  IL_0029:  nop
  // sequence point: int a = 1;
  IL_002a:  ldloca.s   V_8
  IL_002c:  ldc.i4.1
  IL_002d:  dup
  IL_002e:  stloc.0
  IL_002f:  ldc.i4.0
  IL_0030:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_0035:  nop
  // sequence point: int b = 2;
  IL_0036:  ldloca.s   V_8
  IL_0038:  ldc.i4.2
  IL_0039:  dup
  IL_003a:  stloc.1
  IL_003b:  ldc.i4.1
  IL_003c:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_0041:  nop
  // sequence point: p3 = 3;
  IL_0042:  ldloca.s   V_8
  IL_0044:  ldarg.3
  IL_0045:  ldc.i4.3
  IL_0046:  dup
  IL_0047:  stloc.s    V_9
  IL_0049:  stind.i4
  IL_004a:  ldloc.s    V_9
  IL_004c:  ldc.i4.2
  IL_004d:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(uint, ushort)""
  IL_0052:  nop
  // sequence point: ref int r1 = ref a;
  IL_0053:  ldloca.s   V_8
  IL_0055:  ldloca.s   V_0
  IL_0057:  dup
  IL_0058:  stloc.2
  IL_0059:  ldc.i4.2
  IL_005a:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStoreAddress<int>(ref int, ushort)""
  IL_005f:  nop
  IL_0060:  ldloc.2
  IL_0061:  ldind.i4
  IL_0062:  pop
  // sequence point: ref int r2 = ref b
  IL_0063:  ldloca.s   V_8
  IL_0065:  ldloca.s   V_1
  IL_0067:  dup
  IL_0068:  stloc.3
  IL_0069:  ldc.i4.3
  IL_006a:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStoreAddress<int>(ref int, ushort)""
  IL_006f:  nop
  IL_0070:  ldloc.3
  IL_0071:  ldind.i4
  IL_0072:  pop
  // sequence point: r3 = ref p1
  IL_0073:  ldloca.s   V_8
  IL_0075:  ldarga.s   V_1
  IL_0077:  dup
  IL_0078:  stloc.s    V_4
  IL_007a:  ldc.i4.4
  IL_007b:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStoreAddress<int>(ref int, ushort)""
  IL_0080:  nop
  IL_0081:  ldloc.s    V_4
  IL_0083:  ldind.i4
  IL_0084:  pop
  // sequence point: r4 = ref p2
  IL_0085:  ldloca.s   V_8
  IL_0087:  ldarg.2
  IL_0088:  dup
  IL_0089:  stloc.s    V_5
  IL_008b:  ldc.i4.5
  IL_008c:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStoreAddress<int>(ref int, ushort)""
  IL_0091:  nop
  IL_0092:  ldloc.s    V_5
  IL_0094:  ldind.i4
  IL_0095:  pop
  // sequence point: r5 = ref p3
  IL_0096:  ldloca.s   V_8
  IL_0098:  ldarg.3
  IL_0099:  dup
  IL_009a:  stloc.s    V_6
  IL_009c:  ldc.i4.6
  IL_009d:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStoreAddress<int>(ref int, ushort)""
  IL_00a2:  nop
  IL_00a3:  ldloc.s    V_6
  IL_00a5:  ldind.i4
  IL_00a6:  pop
  // sequence point: r6 = ref r1
  IL_00a7:  ldloca.s   V_8
  IL_00a9:  ldloc.2
  IL_00aa:  dup
  IL_00ab:  stloc.s    V_7
  IL_00ad:  ldc.i4.7
  IL_00ae:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStoreAddress<int>(ref int, ushort)""
  IL_00b3:  nop
  IL_00b4:  ldloc.s    V_7
  IL_00b6:  ldind.i4
  IL_00b7:  pop
  // sequence point: if (F(ref r1, ref r2, ref r3, ref r4, ref r5, out r6))
  IL_00b8:  ldarg.0
  IL_00b9:  ldloc.2
  IL_00ba:  ldloc.3
  IL_00bb:  ldloc.s    V_4
  IL_00bd:  ldloc.s    V_5
  IL_00bf:  ldloc.s    V_6
  IL_00c1:  ldloc.s    V_7
  IL_00c3:  call       ""bool C.F(ref int, ref int, ref int, ref int, ref int, out int)""
  IL_00c8:  stloc.s    V_10
  // sequence point: <hidden>
  IL_00ca:  ldloc.s    V_10
  IL_00cc:  brfalse.s  IL_00e5
  // sequence point: {
  IL_00ce:  nop
  // sequence point: r1 = r2;
  IL_00cf:  ldloca.s   V_8
  IL_00d1:  ldloc.2
  IL_00d2:  ldloc.3
  IL_00d3:  ldind.i4
  IL_00d4:  dup
  IL_00d5:  stloc.s    V_9
  IL_00d7:  stind.i4
  IL_00d8:  ldloc.s    V_9
  IL_00da:  ldc.i4.2
  IL_00db:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_00e0:  nop
  IL_00e1:  ldloc.2
  IL_00e2:  ldind.i4
  IL_00e3:  pop
  // sequence point: }
  IL_00e4:  nop
  // sequence point: }
  IL_00e5:  ret
}", sequencePoints: "C.G", source: source);

            verifier.VerifyIL("C.F", @"
 {
  // Code size       38 (0x26)
  .maxstack  4
  .locals init (Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_0,
                int V_1)
  // sequence point: {
  IL_0000:  ldtoken    ""void C.F(ref int)""
  IL_0005:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_000a:  stloc.0
  IL_000b:  ldloca.s   V_0
  IL_000d:  ldarg.1
  IL_000e:  ldc.i4.0
  IL_000f:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStoreAddress<int>(ref int, ushort)""
  IL_0014:  nop
  // sequence point: p = 10;
  IL_0015:  ldloca.s   V_0
  IL_0017:  ldarg.1
  IL_0018:  ldc.i4.s   10
  IL_001a:  dup
  IL_001b:  stloc.1
  IL_001c:  stind.i4
  IL_001d:  ldloc.1
  IL_001e:  ldc.i4.0
  IL_001f:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogParameterStore(uint, ushort)""
  IL_0024:  nop
  // sequence point: }
  IL_0025:  ret
}
", sequencePoints: "C.F", source: source);
        }

        // TODO: shared initializers, constructor calls, etc.
        // 
        [Fact]
        public void Initializers()
        {
            var source = WithHelpers(@"
using System;

class C
{
    static int A = F(out var x) + (x = 1);
    static int B = F(out var x) + (x = 2);

    static int F(out int a) => a = 1;
}
");
            // TODO: odd sequence point:   class C ... }

            var verifier = CompileAndVerify(source);
            verifier.VerifyIL("C..cctor", @"
{
  // Code size       86 (0x56)
  .maxstack  4
  .locals init (Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_0,
                int V_1, //x
                int V_2) //x
  // sequence point: class C ... }
  IL_0000:  ldtoken    ""C..cctor()""
  IL_0005:  ldc.i4.2
  IL_0006:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int, int)""
  IL_000b:  stloc.0
  IL_000c:  ldloca.s   V_0
  IL_000e:  ldloca.s   V_1
  IL_0010:  ldc.i4.1
  IL_0011:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalLoadAddress<int>(ref int, ushort)""
  IL_0016:  ldloca.s   V_0
  IL_0018:  ldloca.s   V_2
  IL_001a:  ldc.i4.2
  IL_001b:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalLoadAddress<int>(ref int, ushort)""
  IL_0020:  nop
  // sequence point: static int A = F(out var x) + (x = 1);
  IL_0021:  ldloca.s   V_1
  IL_0023:  call       ""int C.F(out int)""
  IL_0028:  ldloca.s   V_0
  IL_002a:  ldc.i4.1
  IL_002b:  dup
  IL_002c:  stloc.1
  IL_002d:  ldc.i4.1
  IL_002e:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_0033:  nop
  IL_0034:  ldloc.1
  IL_0035:  add
  IL_0036:  stsfld     ""int C.A""
  // sequence point: static int B = F(out var x) + (x = 2);
  IL_003b:  ldloca.s   V_2
  IL_003d:  call       ""int C.F(out int)""
  IL_0042:  ldloca.s   V_0
  IL_0044:  ldc.i4.2
  IL_0045:  dup
  IL_0046:  stloc.2
  IL_0047:  ldc.i4.2
  IL_0048:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_004d:  nop
  IL_004e:  ldloc.2
  IL_004f:  add
  IL_0050:  stsfld     ""int C.B""
  IL_0055:  ret
}
", sequencePoints: "C..cctor", source: source);
        }

        [Fact]
        public void Initializers_Lambda()
        {
            var source = WithHelpers(@"
using System;

class C
{
    static Action A = new Action(() => { int x = 1; });
}
");

            // TODO: sequence point @ IL_0006 is odd

            var verifier = CompileAndVerify(source);
            verifier.VerifyIL("C..cctor", @"
 {
  // Code size       40 (0x28)
  .maxstack  2
  .locals init (C.<>c__DisplayClass2_0 V_0) //CS$<>8__locals0
  // sequence point: <hidden>
  IL_0000:  newobj     ""C.<>c__DisplayClass2_0..ctor()""
  IL_0005:  stloc.0
  // sequence point: class C ... }
  IL_0006:  ldloc.0
  IL_0007:  ldtoken    ""C..cctor()""
  IL_000c:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int)""
  IL_0011:  stfld      ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker C.<>c__DisplayClass2_0.CS$LocalStoreTracker1""
  // sequence point: static Action A = new Action(() => { int x = 1; });
  IL_0016:  ldloc.0
  IL_0017:  ldftn      ""void C.<>c__DisplayClass2_0.<.cctor>b__0()""
  IL_001d:  newobj     ""System.Action..ctor(object, nint)""
  IL_0022:  stsfld     ""System.Action C.A""
  IL_0027:  ret
}
", sequencePoints: "C..cctor", source: source);

            verifier.VerifyIL("C.<>c__DisplayClass2_0.<.cctor>b__0", @"
{
  // Code size       18 (0x12)
  .maxstack  3
  .locals init (int V_0) //x
  // sequence point: {
  IL_0000:  nop
  // sequence point: int x = 1;
  IL_0001:  ldarg.0
  IL_0002:  ldflda     ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker C.<>c__DisplayClass2_0.CS$LocalStoreTracker1""
  IL_0007:  ldc.i4.1
  IL_0008:  dup
  IL_0009:  stloc.0
  IL_000a:  ldc.i4.0
  IL_000b:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_0010:  nop
  // sequence point: }
  IL_0011:  ret
}
", sequencePoints: "C+<>c__DisplayClass2_0.<.cctor>b__0", source: source);
        }

        [Fact]
        public void EmbeddedStatement()
        {
            var source = WithHelpers(@"
using System;

class C
{
    void M()
    {
        while(true)
            G(F(out var x), x = 1);
    }

    int F(out int a) => a = 1;
    void G(int a, int b) {}
}
");

            var verifier = CompileAndVerify(source);
            verifier.VerifyIL("C.M", @"
{
  // Code size       57 (0x39)
  .maxstack  5
  .locals init (Microsoft.CodeAnalysis.Runtime.LocalStoreTracker V_0,
                int V_1, //x
                bool V_2)
  // sequence point: {
  IL_0000:  ldtoken    ""void C.M()""
  IL_0005:  ldc.i4.1
  IL_0006:  call       ""Microsoft.CodeAnalysis.Runtime.LocalStoreTracker Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogMethodEntry(int, int)""
  IL_000b:  stloc.0
  IL_000c:  ldloca.s   V_0
  IL_000e:  ldloca.s   V_1
  IL_0010:  ldc.i4.1
  IL_0011:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalLoadAddress<int>(ref int, ushort)""
  IL_0016:  nop
  IL_0017:  br.s       IL_0035
  // sequence point: G(F(out var x), x = 1);
  IL_0019:  ldarg.0
  IL_001a:  ldarg.0
  IL_001b:  ldloca.s   V_1
  IL_001d:  call       ""int C.F(out int)""
  IL_0022:  ldloca.s   V_0
  IL_0024:  ldc.i4.1
  IL_0025:  dup
  IL_0026:  stloc.1
  IL_0027:  ldc.i4.1
  IL_0028:  call       ""void Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.LogLocalStore(uint, ushort)""
  IL_002d:  nop
  IL_002e:  ldloc.1
  IL_002f:  call       ""void C.G(int, int)""
  IL_0034:  nop
  // sequence point: while(true)
  IL_0035:  ldc.i4.1
  IL_0036:  stloc.2
  IL_0037:  br.s       IL_0019
}
", sequencePoints: "C.M", source: source);
        }

        [Fact]
        public void StateMachine_VariableWithAddress()
        {
            var source = WithHelpers(@"
using System;
using System.Threading.Tasks;

class C
{
    async Task M()
    {
        F(out var y);
        Console.WriteLine(y);
    }

    int F(out int a) => a = 1;
}
");

            var verifier = CompileAndVerify(source);
            verifier.VerifyIL("C.<M>d__0.MoveNext", @"
", sequencePoints: "C.<M>d__0.MoveNext", source: source);
        }
    }
}
