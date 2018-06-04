// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.


namespace Microsoft.VisualStudio.LanguageServices.Implementation.EditAndContinue.Interop
{
    internal enum EncUnavailableReason
    {
        ENCUN_NONE = 0,
        ENCUN_INTEROP = (ENCUN_NONE + 1),
        ENCUN_SQLCLR = (ENCUN_INTEROP + 1),
        ENCUN_MINIDUMP = (ENCUN_SQLCLR + 1),
        ENCUN_EMBEDDED = (ENCUN_MINIDUMP + 1),
        ENCUN_ATTACH = (ENCUN_EMBEDDED + 1),
        ENCUN_WIN64 = (ENCUN_ATTACH + 1),
        ENCUN_STOPONEMODE = (ENCUN_WIN64 + 1),
        ENCUN_MODULENOTLOADED = (ENCUN_STOPONEMODE + 1),
        ENCUN_MODULERELOADED = (ENCUN_MODULENOTLOADED + 1),
        ENCUN_INRUNMODE = (ENCUN_MODULERELOADED + 1),
        ENCUN_NOTBUILT = (ENCUN_INRUNMODE + 1),
        ENCUN_REMOTE = (ENCUN_NOTBUILT + 1),
        ENCUN_SILVERLIGHT = (ENCUN_REMOTE + 1),
        ENCUN_ENGINE_METRIC_FALSE = (ENCUN_SILVERLIGHT + 1),
        ENCUN_NOT_ALLOWED_FOR_MODULE = (ENCUN_ENGINE_METRIC_FALSE + 1),
        ENCUN_NOT_SUPPORTED_FOR_CLR64_VERSION = (ENCUN_NOT_ALLOWED_FOR_MODULE + 1)
    }
}
