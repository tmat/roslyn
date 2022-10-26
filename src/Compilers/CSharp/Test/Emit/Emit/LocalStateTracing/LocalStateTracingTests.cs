// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.DynamicAnalysis.UnitTests
{
    public class LocalStateTracingTests : CSharpTestBase
    {
        [Fact]
        public void MethodsOfGenericTypesCoverage()
        {
            var helpers = @"
namespace Microsoft.CodeAnalysis.Runtime
{
    internal static class Instrumentation
    {
        public static int LogLocalStore(int value, int methodToken, int localIndex) => value;
        public static object LogLocalStore(object value, int methodToken, int localIndex) => value;
    }
}
";
            var source = helpers + @"

class C
{
    public static void Main()
    {
        int x = 1;
        x = 2;
        for (int i = 0; i < 10; i++)
        {
            if (i % 3 == 0) x += 2;
        }
    }
}
";
            var verifier = CompileAndVerify(source, options: TestOptions.DebugDll, emitOptions: EmitOptions.Default.WithInstrumentationKinds(ImmutableArray.Create(InstrumentationKind.LocalStateTracing)));
            verifier.VerifyIL("C.Main", @"
{
  // Code size       37 (0x25)
  .maxstack  2
  .locals init (int V_0, //x
                int V_1, //i
                bool V_2,
                bool V_3)
  IL_0000:  nop
  IL_0001:  ldc.i4.1
  IL_0002:  stloc.0
  IL_0003:  ldc.i4.0
  IL_0004:  stloc.1
  IL_0005:  br.s       IL_001b
  IL_0007:  nop
  IL_0008:  ldloc.1
  IL_0009:  ldc.i4.3
  IL_000a:  rem
  IL_000b:  ldc.i4.0
  IL_000c:  ceq
  IL_000e:  stloc.2
  IL_000f:  ldloc.2
  IL_0010:  brfalse.s  IL_0016
  IL_0012:  ldloc.0
  IL_0013:  ldc.i4.2
  IL_0014:  add
  IL_0015:  stloc.0
  IL_0016:  nop
  IL_0017:  ldloc.1
  IL_0018:  ldc.i4.1
  IL_0019:  add
  IL_001a:  stloc.1
  IL_001b:  ldloc.1
  IL_001c:  ldc.i4.s   10
  IL_001e:  clt
  IL_0020:  stloc.3
  IL_0021:  ldloc.3
  IL_0022:  brtrue.s   IL_0007
  IL_0024:  ret
}
");
        }
    }
}
