// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>The lexical category of a token.</summary>
public enum TokenKind
{
    /// <summary>The end-of-file marker.</summary>
    EndOfFile,
    /// <summary>An unrecognized or malformed token.</summary>
    Bad,
    /// <summary>An identifier.</summary>
    Identifier,
    /// <summary>A numeric literal.</summary>
    NumericLiteral,
    /// <summary>A string literal.</summary>
    StringLiteral,
    /// <summary>The <c>using</c> keyword.</summary>
    UsingKeyword,
    /// <summary>The <c>namespace</c> keyword.</summary>
    NamespaceKeyword,
    /// <summary>The <c>public</c> keyword.</summary>
    PublicKeyword,
    /// <summary>The <c>internal</c> keyword.</summary>
    InternalKeyword,
    /// <summary>The <c>private</c> keyword.</summary>
    PrivateKeyword,
    /// <summary>The <c>static</c> keyword.</summary>
    StaticKeyword,
    /// <summary>The <c>void</c> keyword.</summary>
    VoidKeyword,
    /// <summary>The <c>var</c> keyword.</summary>
    VarKeyword,
    /// <summary>The <c>mut</c> keyword.</summary>
    MutKeyword,
    /// <summary>The <c>ref</c> keyword.</summary>
    RefKeyword,
    /// <summary>The <c>out</c> keyword.</summary>
    OutKeyword,
    /// <summary>The <c>if</c> keyword.</summary>
    IfKeyword,
    /// <summary>The <c>else</c> keyword.</summary>
    ElseKeyword,
    /// <summary>The <c>while</c> keyword.</summary>
    WhileKeyword,
    /// <summary>The <c>return</c> keyword.</summary>
    ReturnKeyword,
    /// <summary>The <c>true</c> keyword.</summary>
    TrueKeyword,
    /// <summary>The <c>false</c> keyword.</summary>
    FalseKeyword,
    /// <summary>The <c>struct</c> keyword.</summary>
    StructKeyword,
    /// <summary>The <c>class</c> keyword.</summary>
    ClassKeyword,
    /// <summary>The <c>interface</c> keyword.</summary>
    InterfaceKeyword,
    /// <summary>The <c>enum</c> keyword.</summary>
    EnumKeyword,
    /// <summary>The <c>union</c> keyword.</summary>
    UnionKeyword,
    /// <summary>The <c>unique</c> keyword.</summary>
    UniqueKeyword,
    /// <summary>The <c>own</c> keyword.</summary>
    OwnKeyword,
    /// <summary>The <c>move</c> keyword.</summary>
    MoveKeyword,
    /// <summary>The <c>new</c> keyword.</summary>
    NewKeyword,
    /// <summary>The <c>for</c> keyword.</summary>
    ForKeyword,
    /// <summary>The <c>foreach</c> keyword.</summary>
    ForeachKeyword,
    /// <summary>The <c>break</c> keyword.</summary>
    BreakKeyword,
    /// <summary>The <c>continue</c> keyword.</summary>
    ContinueKeyword,
    /// <summary>The <c>switch</c> keyword.</summary>
    SwitchKeyword,
    /// <summary>The <c>case</c> keyword.</summary>
    CaseKeyword,
    /// <summary>The <c>default</c> keyword.</summary>
    DefaultKeyword,
    /// <summary>The <c>null</c> keyword.</summary>
    NullKeyword,
    /// <summary>The <c>async</c> keyword.</summary>
    AsyncKeyword,
    /// <summary>The <c>await</c> keyword.</summary>
    AwaitKeyword,
    /// <summary>The <c>require</c> keyword.</summary>
    RequireKeyword,
    /// <summary>The <c>try</c> keyword.</summary>
    TryKeyword,
    /// <summary>The <c>catch</c> keyword.</summary>
    CatchKeyword,
    /// <summary>The <c>finally</c> keyword.</summary>
    FinallyKeyword,
    /// <summary>The <c>throw</c> keyword.</summary>
    ThrowKeyword,
    /// <summary>An opening parenthesis (<c>(</c>).</summary>
    OpenParen,
    /// <summary>A closing parenthesis (<c>)</c>).</summary>
    CloseParen,
    /// <summary>An opening brace (<c>{</c>).</summary>
    OpenBrace,
    /// <summary>A closing brace (<c>}</c>).</summary>
    CloseBrace,
    /// <summary>An opening bracket (<c>[</c>).</summary>
    OpenBracket,
    /// <summary>A closing bracket (<c>]</c>).</summary>
    CloseBracket,
    /// <summary>A semicolon (<c>;</c>).</summary>
    Semicolon,
    /// <summary>A comma (<c>,</c>).</summary>
    Comma,
    /// <summary>A dot (<c>.</c>).</summary>
    Dot,
    /// <summary>A colon (<c>:</c>).</summary>
    Colon,
    /// <summary>A question mark (<c>?</c>).</summary>
    Question,
    /// <summary>A plus sign (<c>+</c>).</summary>
    Plus,
    /// <summary>A minus sign (<c>-</c>).</summary>
    Minus,
    /// <summary>An asterisk (<c>*</c>).</summary>
    Star,
    /// <summary>A slash (<c>/</c>).</summary>
    Slash,
    /// <summary>A percent sign (<c>%</c>).</summary>
    Percent,
    /// <summary>An exclamation mark (<c>!</c>).</summary>
    Bang,
    /// <summary>A tilde (<c>~</c>).</summary>
    Tilde,
    /// <summary>An equals sign (<c>=</c>).</summary>
    Equals,
    /// <summary>Two equals signs (<c>==</c>).</summary>
    EqualsEquals,
    /// <summary>An exclamation mark followed by an equals sign (<c>!=</c>).</summary>
    BangEquals,
    /// <summary>A less-than sign (<c>&lt;</c>).</summary>
    Less,
    /// <summary>A less-than sign followed by an equals sign (<c>&lt;=</c>).</summary>
    LessEquals,
    /// <summary>A greater-than sign (<c>&gt;</c>).</summary>
    Greater,
    /// <summary>A greater-than sign followed by an equals sign (<c>&gt;=</c>).</summary>
    GreaterEquals,
    /// <summary>An ampersand (<c>&amp;</c>).</summary>
    Ampersand,
    /// <summary>Two ampersands (<c>&amp;&amp;</c>).</summary>
    AmpersandAmpersand,
    /// <summary>A vertical bar (<c>|</c>).</summary>
    Pipe,
    /// <summary>Two vertical bars (<c>||</c>).</summary>
    PipePipe,
    /// <summary>A caret (<c>^</c>).</summary>
    Caret,
    /// <summary>The <c>unsafe</c> keyword, required for checked-failure recovery.</summary>
    UnsafeKeyword,
    /// <summary>Retired source token slot retained for persisted token identity.</summary>
    AbstractKeyword,
    /// <summary>The <c>operator</c> keyword.</summary>
    OperatorKeyword,
    /// <summary>The <c>where</c> keyword.</summary>
    WhereKeyword,
    /// <summary>The write capability qualifier.</summary>
    WriteKeyword,
    /// <summary>The reserved discard marker (<c>_</c>), never an identifier.</summary>
    Underscore,
    /// <summary>The expression-body arrow (<c>=&gt;</c>).</summary>
    Arrow,
    /// <summary>The <c>do</c> keyword.</summary>
    DoKeyword,
    /// <summary>The null-coalescing operator (<c>??</c>).</summary>
    QuestionQuestion,
    /// <summary>The null-coalescing assignment operator (<c>??=</c>).</summary>
    QuestionQuestionEquals,
    /// <summary>The increment operator (<c>++</c>).</summary>
    PlusPlus,
    /// <summary>The decrement operator (<c>--</c>).</summary>
    MinusMinus,
    /// <summary>Left shift.</summary>
    ShiftLeft,
    /// <summary>Arithmetic right shift.</summary>
    ShiftRight,
    /// <summary>Logical right shift.</summary>
    ShiftRightUnsigned,
    /// <summary>Checked integer addition assignment.</summary>
    PlusEquals,
    /// <summary>Checked integer subtraction assignment.</summary>
    MinusEquals,
    /// <summary>Checked integer multiplication assignment.</summary>
    StarEquals,
    /// <summary>Checked integer division assignment.</summary>
    SlashEquals,
    /// <summary>Checked integer remainder assignment.</summary>
    PercentEquals,
    /// <summary>Integer bitwise AND assignment.</summary>
    AmpersandEquals,
    /// <summary>Integer bitwise OR assignment.</summary>
    PipeEquals,
    /// <summary>Integer bitwise XOR assignment.</summary>
    CaretEquals,
    /// <summary>Checked-count truncating left shift assignment.</summary>
    ShiftLeftEquals,
    /// <summary>Checked-count arithmetic right shift assignment.</summary>
    ShiftRightEquals,
    /// <summary>Checked-count zero-filling right shift assignment.</summary>
    ShiftRightUnsignedEquals,
    /// <summary>The <c>alias</c> keyword.</summary>
    AliasKeyword,
    /// <summary>A single-scalar character literal.</summary>
    CharacterLiteral,
    /// <summary>A range connector (<c>..</c>).</summary>
    DotDot,
    /// <summary>A range connector with an end-relative upper bound (<c>..^</c>).</summary>
    DotDotCaret,
    /// <summary>Optional member access (<c>?.</c>).</summary>
    QuestionDot,
    /// <summary>The <c>extern</c> linkage keyword.</summary>
    ExternKeyword,
}
