using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.VisualStudio.Debugger.Evaluation
{
    internal enum DkmFullNameOperationKind
    {
        MemberAccess = 1,
        ArrayIndex = 1 << 1,
        MultiDimensionalArrayIndex = 1 << 2
    }

    internal readonly struct DkmFullNameOperation
    {
        public readonly DkmFullNameOperationKind Kind;
        public readonly string[] Arguments;

        private DkmFullNameOperation(DkmFullNameOperationKind kind, string[] arguments)
        {
            Kind = kind;
            Arguments = arguments;
        }
    }

    internal readonly struct DkmFullNameDescriptor
    {
        public readonly string BaseExpression;
        public readonly DkmFullNameOperation[] Operations;

        public DkmFullNameDescriptor(string baseExpression, DkmFullNameOperation[] operations = null)
        {
            BaseExpression = baseExpression;
            Operations = operations ?? Array.Empty<DkmFullNameOperation>();
        }

        public bool IsDefault => BaseExpression is null;
    }
}
