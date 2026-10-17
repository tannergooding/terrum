// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Syntax;

/// <summary>The syntactic construct represented by a node.</summary>
public enum SyntaxKind
{
    /// <summary>A complete source file.</summary>
    CompilationUnit,
    /// <summary>A <c>using</c> directive.</summary>
    UsingDirective,
    /// <summary>A namespace declaration.</summary>
    NamespaceDeclaration,
    /// <summary>A function declaration.</summary>
    FunctionDeclaration,
    /// <summary>A structure declaration.</summary>
    StructDeclaration,
    /// <summary>A sealed managed reference type declaration.</summary>
    ClassDeclaration,
    /// <summary>A constructor declaration.</summary>
    ConstructorDeclaration,
    /// <summary>A field declaration.</summary>
    FieldDeclaration,
    /// <summary>A by-value property with an explicit getter.</summary>
    PropertyDeclaration,
    /// <summary>An explicit getter and its local effect attributes.</summary>
    GetAccessor,
    /// <summary>A type name.</summary>
    TypeName,
    /// <summary>The local fixed-array type name <c>InlineArray&lt;T, N&gt;</c>.</summary>
    InlineArrayTypeName,
    /// <summary>A function or constructor parameter list.</summary>
    ParameterList,
    /// <summary>A declared parameter.</summary>
    Parameter,
    /// <summary>A braced statement block.</summary>
    Block,
    /// <summary>A variable declaration.</summary>
    VariableDeclaration,
    /// <summary>A local awaiting checked initialization.</summary>
    VacantLocalDeclaration,
    /// <summary>A local whose initializer must evaluate to a compile-time scalar value.</summary>
    ConstDeclaration,
    /// <summary>An expression required to evaluate at compile time.</summary>
    ConstExpression,
    /// <summary>An <c>if</c> statement.</summary>
    IfStatement,
    /// <summary>An <c>else</c> clause.</summary>
    ElseClause,
    /// <summary>A <c>while</c> statement.</summary>
    WhileStatement,
    /// <summary>A <c>return</c> statement.</summary>
    ReturnStatement,
    /// <summary>An expression used as a statement.</summary>
    ExpressionStatement,
    /// <summary>An assignment statement.</summary>
    AssignmentStatement,
    /// <summary>Evaluates and discards one expression without introducing a named destination.</summary>
    DiscardStatement,
    /// <summary>A statement consisting only of a semicolon.</summary>
    EmptyStatement,
    /// <summary>An identifier expression.</summary>
    NameExpression,
    /// <summary>A literal expression.</summary>
    LiteralExpression,
    /// <summary>An expression enclosed in parentheses.</summary>
    ParenthesizedExpression,
    /// <summary>An expression whose caller-unsafe obligations are explicitly authorized.</summary>
    UnsafeExpression,
    /// <summary>An empty parenthesized expression (<c>()</c>).</summary>
    VoidExpression,
    /// <summary>A unary operator expression.</summary>
    UnaryExpression,
    /// <summary>An explicit cast.</summary>
    CastExpression,
    /// <summary>A binary operator expression.</summary>
    BinaryExpression,
    /// <summary>A function invocation expression.</summary>
    InvocationExpression,
    /// <summary>An object creation expression.</summary>
    ObjectCreationExpression,
    /// <summary>A member access expression.</summary>
    MemberAccessExpression,
    /// <summary>An indexed access expression.</summary>
    IndexExpression,
    /// <summary>A bracket initializer, admitted only by a closed local storage gate.</summary>
    ArrayInitializerExpression,
    /// <summary>A half-open slice range used as an index argument.</summary>
    SliceRangeExpression,
    /// <summary>An intentionally omitted lower or upper slice bound.</summary>
    OmittedIndexBound,
    /// <summary>A contextual end-relative index or slice bound.</summary>
    EndRelativeIndexBound,
    /// <summary>An invocation or creation argument list.</summary>
    ArgumentList,
    /// <summary>An individual argument.</summary>
    Argument,
    /// <summary>An absent expression represented during parser recovery.</summary>
    MissingExpression,
    /// <summary>Unrecognized source preserved during parser recovery.</summary>
    SkippedSyntax,
    /// <summary>A checked boolean requirement statement.</summary>
    RequireStatement,
    /// <summary>A body-local compile-time-only point obligation.</summary>
    StaticRequireStatement,
    /// <summary>A body-local compile-time-only outer normal-return obligation.</summary>
    StaticEnsureStatement,
    /// <summary>A scoped <c>for</c> loop with optional initializer, condition and iterator.</summary>
    ForStatement,
    /// <summary>An intentionally omitted optional expression, such as a for-loop condition.</summary>
    OmittedExpression,
    /// <summary>A parameterless declaration attribute, including its brackets.</summary>
    Attribute,
    /// <summary>A structured body and its unconditional normal-exit cleanup.</summary>
    TryFinallyStatement,
    /// <summary>An explicit unsafe context for checked-failure recovery.</summary>
    UnsafeStatement,
    /// <summary>A structured checked-failure handler with optional cleanup.</summary>
    TryCatchStatement,
    /// <summary>A compilation-local static operator protocol.</summary>
    InterfaceDeclaration,
    /// <summary>A static operator declaration or implementation.</summary>
    OperatorDeclaration,
    /// <summary>Declared type parameters of a protocol or function.</summary>
    TypeParameterList,
    /// <summary>An explicit nominal conformance list.</summary>
    ConformanceList,
    /// <summary>A generic function's declared constraints.</summary>
    ConstraintClause,
    /// <summary>An explicitly instantiated generic function name.</summary>
    GenericNameExpression,
    /// <summary>A test for an absent optional managed handle.</summary>
    IsNullExpression,
    /// <summary>A test that binds a non-null managed-handle snapshot on success.</summary>
    IsSomeExpression,
    /// <summary>Exits an enclosing loop.</summary>
    BreakStatement,
    /// <summary>Continues an enclosing loop.</summary>
    ContinueStatement,
    /// <summary>A single-statement callable body introduced by <c>=&gt;</c>.</summary>
    ExpressionBody,
    /// <summary>A post-tested loop.</summary>
    DoStatement,
    /// <summary>A loop with an explicitly named transfer target.</summary>
    LabeledLoopStatement,
    /// <summary>A right-associative conditional choice.</summary>
    ConditionalExpression,
    /// <summary>A prefix or postfix update of an initialized writable Copy place.</summary>
    UpdateExpression,
    /// <summary>A built-in integer compound assignment statement.</summary>
    CompoundAssignmentStatement,
    /// <summary>Ends the current value of a named local without ending its declaration.</summary>
    LifetimeEndStatement,
    /// <summary>A nullable inferred-binding pattern, including initialized null.</summary>
    IsVarNullableExpression,
    /// <summary>A direct initialization-state test.</summary>
    IsInitializedExpression,
    /// <summary>A transparent namespace-level type alias declaration.</summary>
    AliasDeclaration,
    /// <summary>A bodyless callable declaration terminated by a semicolon.</summary>
    ExternalBody,
    /// <summary>A source-ordered collection spread element beginning with <c>..</c>.</summary>
    CollectionSpreadElement,
}
