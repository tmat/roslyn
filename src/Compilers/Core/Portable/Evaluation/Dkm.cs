using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.CodeAnalysis.ExpressionEvaluator
{
    [Flags]
    public enum ClrCompilationResultFlags
    {
        None = 0,
        PotentialSideEffect = 1,
        ReadOnlyResult = 2,
        BoolResult = 4
    }

    public enum ClrAliasKind
    {
        Exception = 0,
        StowedException = 1,
        ReturnValue = 2,
        Variable = 3,
        ObjectId = 4
    }

    [Flags]
    public enum EvaluationFlags
    {
        None = 0,
        TreatAsExpression = 1,
        TreatFunctionAsAddress = 2,
        NoSideEffects = 4,
        NoFuncEval = 8,
        DesignTime = 16,
        AllowImplicitVariables = 32,
        ForceEvaluationNow = 64,
        ShowValueRaw = 128,
        ForceRealFuncEval = 256,
        HideNonPublicMembers = 512,
        NoToString = 1024,
        NoFormatting = 2048,
        NoRawView = 4096,
        NoQuotes = 8192,
        DynamicView = 16384,
        ResultsOnly = 32768,
        NoExpansion = 65536,
        EnableExtendedSideEffects = 131072
    }
}
