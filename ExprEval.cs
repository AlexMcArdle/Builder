// ExprEval.cs — lightweight arithmetic expression evaluator.
//
// Supports: named variables, float literals, +  -  *  /  ( )  unary minus.
// Variable names may contain letters, digits, and underscores.
//
// Examples:
//   "2.5"                         → 2.5
//   "-6.9"                        → -6.9
//   "WALL_BASE + FLOOR1_H / 2"    → 2.5  (given WALL_BASE=0.5, FLOOR1_H=4.0)
//   "FLOOR1_TOP - 0.6"            → 3.9  (given FLOOR1_TOP=4.5)
//   "(ROOF_BASE + 4.5) / 2"       → 5.6  (given ROOF_BASE=6.7)

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SLHouseBuilder
{
    static class ExprEval
    {
        public static float Eval(string expr, Dictionary<string, float> vars)
        {
            if (string.IsNullOrWhiteSpace(expr)) return 0f;
            var tokens = Tokenize(expr.Trim());
            int pos = 0;
            float result = ParseExpr(tokens, ref pos, vars);
            if (pos < tokens.Count)
                throw new Exception($"Unexpected token '{tokens[pos]}' in expression: {expr}");
            return result;
        }

        // ── Grammar ──────────────────────────────────────────────────────────────
        // expr   = term   ( ('+' | '-') term   )*
        // term   = factor ( ('*' | '/') factor )*
        // factor = '-' factor | '(' expr ')' | NUMBER | NAME

        static float ParseExpr(List<string> t, ref int pos, Dictionary<string, float> vars)
        {
            float val = ParseTerm(t, ref pos, vars);
            while (pos < t.Count && (t[pos] == "+" || t[pos] == "-"))
            {
                string op = t[pos++];
                float r = ParseTerm(t, ref pos, vars);
                val = op == "+" ? val + r : val - r;
            }
            return val;
        }

        static float ParseTerm(List<string> t, ref int pos, Dictionary<string, float> vars)
        {
            float val = ParseFactor(t, ref pos, vars);
            while (pos < t.Count && (t[pos] == "*" || t[pos] == "/"))
            {
                string op = t[pos++];
                float r = ParseFactor(t, ref pos, vars);
                val = op == "*" ? val * r : val / r;
            }
            return val;
        }

        static float ParseFactor(List<string> t, ref int pos, Dictionary<string, float> vars)
        {
            if (pos >= t.Count) throw new Exception("Unexpected end of expression.");

            if (t[pos] == "-") { pos++; return -ParseFactor(t, ref pos, vars); }

            if (t[pos] == "(")
            {
                pos++;
                float val = ParseExpr(t, ref pos, vars);
                if (pos >= t.Count || t[pos] != ")")
                    throw new Exception("Missing closing parenthesis.");
                pos++;
                return val;
            }

            string tok = t[pos++];

            if (float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                return f;

            if (vars.TryGetValue(tok, out float v))
                return v;

            throw new Exception($"Unknown identifier '{tok}' — not a number or defined constant.");
        }

        // ── Tokenizer ─────────────────────────────────────────────────────────────
        static List<string> Tokenize(string expr)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < expr.Length)
            {
                char c = expr[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (c is '+' or '-' or '*' or '/' or '(' or ')')
                    { tokens.Add(c.ToString()); i++; continue; }

                if (char.IsDigit(c) || c == '.')
                {
                    int start = i;
                    while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.')) i++;
                    // scientific notation: e.g. 1.5e+3
                    if (i < expr.Length && (expr[i] == 'e' || expr[i] == 'E'))
                    {
                        i++;
                        if (i < expr.Length && (expr[i] == '+' || expr[i] == '-')) i++;
                        while (i < expr.Length && char.IsDigit(expr[i])) i++;
                    }
                    tokens.Add(expr.Substring(start, i - start));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_')) i++;
                    tokens.Add(expr.Substring(start, i - start));
                    continue;
                }

                throw new Exception($"Unexpected character '{c}' in expression: {expr}");
            }
            return tokens;
        }
    }
}
