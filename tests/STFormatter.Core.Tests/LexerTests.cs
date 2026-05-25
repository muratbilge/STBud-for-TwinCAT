using STFormatter.Core.Lexing;
using STFormatter.Core.Syntax;
using STFormatter.Core.Text;

namespace STFormatter.Core.Tests;

public class LexerTests
{
    [Fact]
    public void Lexer_Tokenizes_SimpleKeywords()
    {
        var text = SourceText.From("PROGRAM END_PROGRAM");
        var lexer = new Lexer(text);

        var tokens = new List<SyntaxToken>();
        SyntaxToken token;
        do
        {
            token = lexer.Lex();
            tokens.Add(token);
        } while (token.Kind != SyntaxKind.EndOfFile);

        Assert.Equal(3, tokens.Count);
        Assert.Equal(SyntaxKind.ProgramKeyword, tokens[0].Kind);
        Assert.Equal(SyntaxKind.EndProgramKeyword, tokens[1].Kind);
        Assert.Equal(SyntaxKind.EndOfFile, tokens[2].Kind);
    }

    [Fact]
    public void Lexer_Tokenizes_Identifier()
    {
        var text = SourceText.From("MyVariable");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.Identifier, token.Kind);
        Assert.Equal("MyVariable", token.Text);
    }

    [Fact]
    public void Lexer_Tokenizes_NumericLiteral()
    {
        var text = SourceText.From("123");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.NumericLiteral, token.Kind);
        Assert.Equal(123, token.Value);
    }

    [Fact]
    public void Lexer_Tokenizes_HexNumber()
    {
        var text = SourceText.From("16#FF");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.NumericLiteral, token.Kind);
        Assert.Equal(255, token.Value);
    }

    [Fact]
    public void Lexer_Tokenizes_StringLiteral()
    {
        var text = SourceText.From("'Hello World'");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.StringLiteral, token.Kind);
        Assert.Equal("Hello World", token.Value);
    }

    [Fact]
    public void Lexer_Tokenizes_TimeLiteral()
    {
        var text = SourceText.From("T#500ms");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.TimeLiteral, token.Kind);
        Assert.Equal("T#500ms", token.Text);
    }

    [Fact]
    public void Lexer_Tokenizes_AssignmentOperator()
    {
        var text = SourceText.From(":=");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.AssignmentOperator, token.Kind);
    }

    [Fact]
    public void Lexer_Tokenizes_ComparisonOperators()
    {
        var operators = new Dictionary<string, SyntaxKind>
        {
            { "=", SyntaxKind.Equal },
            { "<>", SyntaxKind.NotEqual },
            { "<", SyntaxKind.LessThan },
            { ">", SyntaxKind.GreaterThan },
            { "<=", SyntaxKind.LessThanOrEqual },
            { ">=", SyntaxKind.GreaterThanOrEqual }
        };

        foreach (var op in operators)
        {
            var text = SourceText.From(op.Key);
            var lexer = new Lexer(text);
            var token = lexer.Lex();

            Assert.Equal(op.Value, token.Kind);
        }
    }

    [Fact]
    public void Lexer_Tokenizes_DirectVariable()
    {
        var text = SourceText.From("%MW100");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.DirectVariable, token.Kind);
    }

    [Fact]
    public void Lexer_Tokenizes_WithLeadingTrivia()
    {
        var text = SourceText.From("   PROGRAM");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.ProgramKeyword, token.Kind);
        Assert.Single(token.LeadingTrivia);
        Assert.Equal(SyntaxKind.WhitespaceTrivia, token.LeadingTrivia[0].Kind);
    }

    [Fact]
    public void Lexer_Tokenizes_SingleLineComment()
    {
        var text = SourceText.From("// This is a comment\nPROGRAM");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.ProgramKeyword, token.Kind);
        Assert.True(token.LeadingTrivia.Length >= 1);
        Assert.Equal(SyntaxKind.SingleLineCommentTrivia, token.LeadingTrivia[0].Kind);
        Assert.Equal("// This is a comment", token.LeadingTrivia[0].Text);
    }

    [Fact]
    public void Lexer_Tokenizes_MultiLineComment()
    {
        var text = SourceText.From("(* This is a comment *)PROGRAM");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.ProgramKeyword, token.Kind);
        Assert.Single(token.LeadingTrivia);
        Assert.Equal(SyntaxKind.MultiLineCommentTrivia, token.LeadingTrivia[0].Kind);
    }

    [Fact]
    public void Lexer_Tokenizes_Pragma()
    {
        var text = SourceText.From("{attribute 'hide'}PROGRAM");
        var lexer = new Lexer(text);
        var token = lexer.Lex();

        Assert.Equal(SyntaxKind.ProgramKeyword, token.Kind);
        Assert.Single(token.LeadingTrivia);
        Assert.Equal(SyntaxKind.PragmaTrivia, token.LeadingTrivia[0].Kind);
    }

    [Fact]
    public void Lexer_Handles_CaseInsensitiveKeywords()
    {
        var text = SourceText.From("program end_program");
        var lexer = new Lexer(text);

        var token1 = lexer.Lex();
        var token2 = lexer.Lex();

        Assert.Equal(SyntaxKind.ProgramKeyword, token1.Kind);
        Assert.Equal(SyntaxKind.EndProgramKeyword, token2.Kind);
    }
}
