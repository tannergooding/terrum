# Third-party notices

## Unicode Character Database

The generated `sources/Terrum.CodeAnalysis/Lexing/UnicodeXid.cs` contains
derived `XID_Start` and `XID_Continue` ranges from Unicode 17.0.0
`DerivedCoreProperties.txt`, downloaded from
https://www.unicode.org/Public/17.0.0/ucd/DerivedCoreProperties.txt .
The input SHA-256 is
`24c7fed1195c482faaefd5c1e7eb821c5ee1fb6de07ecdbaa64b56a99da22c08`.
Regenerate with `python scripts/unicode/generate-xid.py` from the repository
root. The following notice applies to the data:

> UNICODE LICENSE V3
>
> COPYRIGHT AND PERMISSION NOTICE
>
> Copyright © 1991-2026 Unicode, Inc.
>
> NOTICE TO USER: Carefully read the following legal agreement. BY
> DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
> SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
> TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
> DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.
>
> Permission is hereby granted, free of charge, to any person obtaining a
> copy of data files and any associated documentation (the "Data Files") or
> software and any associated documentation (the "Software") to deal in
> the Data Files or Software without restriction, including without
> limitation the rights to use, copy, modify, merge, publish, distribute,
> and/or sell copies of the Data Files or Software, and to permit persons to
> whom the Data Files or Software are furnished to do so, provided that
> either (a) this copyright and permission notice appear with all copies of
> the Data Files or Software, or (b) this copyright and permission notice
> appear in associated Documentation.
>
> THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF
> ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
> WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT OF THIRD PARTY RIGHTS.
>
> IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
> BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL
> DAMAGES, OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR
> PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS
> ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE
> DATA FILES OR SOFTWARE.
>
> Except as contained in this notice, the name of a copyright holder shall
> not be used in advertising or otherwise to promote the sale, use or other
> dealings in these Data Files or Software without prior written
> authorization of the copyright holder.

## TerraFX lexer gist

The MIT-licensed historical `Lexer.cs`, `SourceText.cs`, `SourceSpan.cs`,
`Token.cs`, and `TokenKind.cs` by Tanner Gooding and Contributors were
consulted as prior art:
https://gist.github.com/tannergooding/9149d45cdc0e6d46414153908b671f28 .
No source was copied into this implementation. The gist's original copyright
notice is “Copyright © Tanner Gooding and Contributors. Licensed under the
MIT License (MIT).” See the repository's `LICENSE.md` for the MIT license.
