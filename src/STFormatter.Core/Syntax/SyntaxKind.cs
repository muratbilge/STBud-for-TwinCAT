namespace STFormatter.Core.Syntax;

public enum SyntaxKind
{
    // Special
    None,
    BadToken,
    EndOfFile,
    MissingToken,
    SkippedTokens,

    // Trivia
    WhitespaceTrivia,
    LineBreakTrivia,
    SingleLineCommentTrivia,
    MultiLineCommentTrivia,
    PragmaTrivia,
    DisabledTextTrivia,

    // Literals
    NumericLiteral,
    StringLiteral,
    BoolLiteral,
    TimeLiteral,
    DateLiteral,
    TimeOfDayLiteral,
    DateAndTimeLiteral,
    BitStringLiteral,
    RealLiteral,

    // Identifiers
    Identifier,
    TypeIdentifier,

    // Direct Variables
    DirectVariable,

    // Keywords - Program Organization Units
    ProgramKeyword,
    FunctionBlockKeyword,
    FunctionKeyword,
    MethodKeyword,
    PropertyKeyword,
    ActionKeyword,
    TransitionKeyword,
    StepKeyword,
    InitialStepKeyword,
    EndProgramKeyword,
    EndFunctionBlockKeyword,
    EndFunctionKeyword,
    EndMethodKeyword,
    EndPropertyKeyword,
    EndActionKeyword,
    EndTransitionKeyword,
    EndStepKeyword,

    // Keywords - Variable Sections
    VarKeyword,
    VarInputKeyword,
    VarOutputKeyword,
    VarInOutKeyword,
    VarTempKeyword,
    VarStatKeyword,
    VarGlobalKeyword,
    VarAccessKeyword,
    VarExternalKeyword,
    VarConfigKeyword,
    VarInstKeyword,
    EndVarKeyword,
    ConstantKeyword,
    RetainKeyword,
    PersistentKeyword,
    ReadOnlyKeyword,
    ReadWriteKeyword,

    // Keywords - Control Statements
    IfKeyword,
    ThenKeyword,
    ElseKeyword,
    ElsIfKeyword,
    EndIfKeyword,
    CaseKeyword,
    OfKeyword,
    EndCaseKeyword,
    ForKeyword,
    ToKeyword,
    ByKeyword,
    DoKeyword,
    EndForKeyword,
    WhileKeyword,
    EndWhileKeyword,
    RepeatKeyword,
    UntilKeyword,
    EndRepeatKeyword,
    ExitKeyword,
    ContinueKeyword,
    ReturnKeyword,
    GotoKeyword,

    // Keywords - Types
    ArrayKeyword,
    StructKeyword,
    EndStructKeyword,
    TypeKeyword,
    EndTypeKeyword,
    UnionKeyword,
    EndUnionKeyword,
    EnumKeyword,
    EndEnumKeyword,
    StringKeyword,
    WStringKeyword,
    PointerKeyword,
    RefToKeyword,
    ReferenceKeyword,
    AtKeyword,
    EdgeKeyword,
    REdgeKeyword,
    FEdgeKeyword,

    // Keywords - OOP / TwinCAT
    ThisKeyword,
    SuperKeyword,
    PublicKeyword,
    PrivateKeyword,
    ProtectedKeyword,
    InternalKeyword,
    FinalKeyword,
    AbstractKeyword,
    OverrideKeyword,
    ExtendsKeyword,
    ImplementsKeyword,
    InterfaceKeyword,
    EndInterfaceKeyword,
    GetKeyword,
    SetKeyword,
    UsingKeyword,
    FromKeyword,
    WithKeyword,

    // Keywords - Exception Handling (TwinCAT)
    TryKeyword,
    CatchKeyword,
    FinallyKeyword,
    EndTryKeyword,
    RaiseKeyword,

    // Keywords - Other
    TrueKeyword,
    FalseKeyword,
    NullKeyword,
    VoidKeyword,

    // Elementary Type Keywords
    BoolKeyword,
    ByteKeyword,
    WordKeyword,
    DWordKeyword,
    LWordKeyword,
    SIntKeyword,
    IntKeyword,
    DIntKeyword,
    LIntKeyword,
    USIntKeyword,
    UIntKeyword,
    UDIntKeyword,
    ULIntKeyword,
    RealKeyword,
    LRealKeyword,
    TimeKeyword,
    LTimeKeyword,
    DateKeyword,
    TODKeyword,
    TimeOfDayTypeKeyword,
    DTKeyword,
    DateAndTimeTypeKeyword,

    // Operators
    AssignmentOperator,          // :=
    ColonEqual,                  // := (alias)
    ArrowOperator,               // =>
    Colon,                       // :
    Semicolon,                   // ;
    Comma,                       // ,
    Dot,                         // .
    DotDot,                      // ..
    Plus,                        // +
    Minus,                       // -
    Asterisk,                    // *
    Slash,                       // /
    Power,                       // **
    Equal,                       // =
    NotEqual,                    // <>
    LessThan,                    // <
    GreaterThan,                 // >
    LessThanOrEqual,             // <=
    GreaterThanOrEqual,          // >=
    Hash,                        // #
    Ampersand,                   // &
    ModKeyword,                  // MOD
    AndKeyword,                  // AND
    OrKeyword,                   // OR
    XorKeyword,                  // XOR
    NotKeyword,                  // NOT
    ShlKeyword,                  // SHL
    ShrKeyword,                  // SHR
    RolKeyword,                  // ROL
    RorKeyword,                  // ROR

    // Delimiters
    OpenParen,                   // (
    CloseParen,                  // )
    OpenBracket,                 // [
    CloseBracket,                // ]
    OpenBrace,                   // {
    CloseBrace,                  // }

    // Nodes - Compilation
    CompilationUnit,

    // Nodes - POU Declarations
    ProgramDeclaration,
    FunctionBlockDeclaration,
    FunctionDeclaration,
    MethodDeclaration,
    PropertyDeclaration,
    ActionDeclaration,
    TransitionDeclaration,
    StepDeclaration,

    // Nodes - Variable Sections
    VarSection,
    VarGlobalSection,
    VarAccessSection,
    VarConfigSection,
    VarExternalSection,

    // Nodes - Variables
    VariableDeclaration,
    VariableInitializer,
    VarInitDecl,

    // Nodes - Types
    NamedType,
    ArrayType,
    ArrayRange,
    RangeExpression,
    StructuredType,
    UnionType,
    StructureElement,
    StringType,
    EnumerationType,
    EnumValue,
    ReferenceType,
    PointerType,
    SubrangeType,

    // Nodes - Statements
    Statement,
    EmptyStatement,
    AssignmentStatement,
    CallStatement,
    IfStatement,
    IfThenBlock,
    ElseClause,
    ElsIfClause,
    CaseStatement,
    CaseClause,
    ElseCaseClause,
    ForStatement,
    WhileStatement,
    RepeatStatement,
    ExitStatement,
    ContinueStatement,
    ReturnStatement,
    GotoStatement,
    LabelStatement,
    StatementList,
    TryStatement,
    CatchClause,
    FinallyClause,

    // Nodes - Expressions
    Expression,
    LiteralExpression,
    IdentifierExpression,
    MemberAccessExpression,
    ElementAccessExpression,
    InvocationExpression,
    Argument,
    NamedArgument,
    BinaryExpression,
    UnaryExpression,
    ParenthesizedExpression,
    InitializerExpression,
    StructuredInitializer,
    ArrayInitializer,
    DirectVariableExpression,

    // Nodes - Other
    Attribute,
    AttributeList,
    PragmaDirective,
    RegionDirective,
    EndRegionDirective,
    UsingDirective,
    InitialStepClause,

    // Missing kinds needed by parser
    GetAccessor,
    SetAccessor,
    OutputAssignmentStatement,
    ForByClause,
    AtClause,
    StringLength,
    EnumValueInitializer,
    ExtendsClause,
    ImplementsClause,
    InterfaceDeclaration,
    TypeDeclaration,

    // Helper for formatting
    CompilationUnitSyntax
}
