// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.EditAndContinue
{
    internal struct ActiveStatement
    {
        public readonly int Id;
        public readonly ActiveStatementFlags Flags;

        public ActiveStatement(int id, ActiveStatementFlags flags)
        {
            Id = id;
            Flags = flags;
        }
    }
}
