using System.Text.RegularExpressions;

namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// Rewrites individual <c>constexpr</c> constants inside a copy of the firmware's
/// <c>Config.example.h</c>, anchoring on the constant's <b>name</b> rather than on a
/// placeholder cut into the text.
///
/// <para>
/// This is what lets the embedded template be a <em>verbatim</em> copy of the
/// firmware's own file. The previous design cut <c>{{TOKEN}}</c> holes into a
/// hand-maintained copy, which meant every new firmware constant needed a matching
/// hand edit here — and one day it did not get one: three fix-averaging constants
/// were added to the firmware and the copy went out of date, so the dashboard served
/// a <c>Config.h</c> that no longer compiled. With name anchoring, a constant nobody
/// rewrites simply passes through carrying its example value, so the drift has
/// nowhere to come from.
/// </para>
///
/// <para>
/// It is the deliberate mirror of the browser's <c>FE/src/utils/configSecrets.ts</c>,
/// which fills the operator's four secrets into the same file with the same anchored
/// regexes. Keep the two in step: they edit one file between them.
/// </para>
///
/// <para>
/// <b>Every method throws when its anchor does not match</b>, and that is the whole
/// safety story of this class. A missing <c>{{TOKEN}}</c> used to be visible in the
/// output — you could see the braces. A rename that a name-anchored replace silently
/// skips is invisible: the file still compiles and the tracker publishes to
/// <c>devices/GNSSXX</c>, the example's placeholder id, instead of its own topic.
/// Failing the provisioning request outright is far cheaper than diagnosing that from
/// a tracker in the field.
/// </para>
/// </summary>
internal sealed class ConfigConstantWriter
{
    /// <summary>
    /// A rewrite that finds no anchor is a packaging/rename error affecting every
    /// device, not one request, so the failure is worth a hard stop. Regexes are
    /// built per call rather than cached: this runs a few dozen times per
    /// provisioning request, which is nothing next to the RSA work around it.
    /// </summary>
    private const RegexOptions LineOptions = RegexOptions.Multiline | RegexOptions.CultureInvariant;

    /// <summary>The file as it stands, mutated in place by each rewrite.</summary>
    private string _text;

    /// <summary>Starts a rewrite session over a copy of the firmware template.</summary>
    /// <param name="template">The template text, with <c>\n</c> line endings.</param>
    public ConfigConstantWriter(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        _text = template;
    }

    /// <summary>The rewritten file.</summary>
    /// <returns>The current text.</returns>
    public override string ToString()
    {
        return _text;
    }

    /// <summary>
    /// Replaces the value of <c>constexpr char name[] = "...";</c>, keeping whatever
    /// column alignment and trailing comment the line already carries.
    /// </summary>
    /// <param name="name">The constant's name, e.g. <c>kDeviceId</c>.</param>
    /// <param name="value">
    /// The new value, unescaped. May be empty — unlike the browser's helper, which
    /// skips blanks because a blank there means "the operator typed nothing". Here a
    /// blank is an instruction: it is how the four secrets are forced empty even if
    /// the firmware's template were ever committed with one filled in.
    /// </param>
    public void SetString(string name, string value)
    {
        // The value pattern tolerates escapes so a template value containing \" is
        // matched whole rather than up to the escaped quote.
        Replace(
            name,
            new Regex($@"^(constexpr\s+char\s+{Regex.Escape(name)}\[\]\s*=\s*)""(?:[^""\\]|\\.)*""", LineOptions),
            match => match.Groups[1].Value + '"' + EscapeCString(value) + '"');

        if (value.Length > 0)
        {
            DropTrailingHint(name);
        }
    }

    /// <summary>
    /// Removes a trailing <c>// &lt;-- set this yourself</c> hint from a constant the
    /// server has just filled in.
    ///
    /// <para>
    /// The firmware's template carries those hints for a person copying the file by
    /// hand, and on the four constants that stay blank they are still exactly right —
    /// so they are left alone there. On the one the API does fill in
    /// (<c>kMqttBrokerUri</c>, whose hint reads "leave blank here") the advice does not
    /// merely go stale, it contradicts the value sitting next to it — which is how an
    /// operator ends up deleting a broker URI that was already correct.
    /// </para>
    /// </summary>
    /// <param name="name">The constant whose line to tidy.</param>
    private void DropTrailingHint(string name)
    {
        // Deliberately not routed through Replace(): most constants carry no hint at
        // all, so finding none is the normal case here rather than a rename to shout
        // about.
        Regex hint = new Regex(
            $@"^(constexpr\s+char\s+{Regex.Escape(name)}\[\]\s*=\s*""(?:[^""\\]|\\.)*"";)[ \t]*//[ \t]*<--[^\n]*",
            LineOptions);

        _text = hint.Replace(_text, match => match.Groups[1].Value, 1);
    }

    /// <summary>Replaces the value of <c>constexpr bool name = true|false;</c>.</summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="value">The new value.</param>
    public void SetBool(string name, bool value)
    {
        Replace(
            name,
            new Regex($@"^(constexpr\s+bool\s+{Regex.Escape(name)}\s*=\s*)(?:true|false)", LineOptions),
            match => match.Groups[1].Value + (value ? "true" : "false"));
    }

    /// <summary>
    /// Replaces the value of an integral <c>constexpr</c> constant, whatever its type
    /// (<c>uint32_t</c>, <c>uint8_t</c>, <c>int</c>, …) — the type is matched rather
    /// than named, so a widening on the firmware side does not break this.
    /// </summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="value">The new value, already rendered invariantly.</param>
    public void SetNumber(string name, string value)
    {
        bool changed = false;

        Replace(
            name,
            new Regex($@"^(constexpr\s+[A-Za-z_]\w*\s+{Regex.Escape(name)}\s*=\s*)(\d+)", LineOptions),
            match =>
            {
                changed = !string.Equals(match.Groups[2].Value, value, StringComparison.Ordinal);

                return match.Groups[1].Value + value;
            });

        if (changed)
        {
            DropTrailingGloss(name);
        }
    }

    /// <summary>
    /// Removes the trailing <c>// 24 h</c>-style gloss from a numeric constant whose
    /// value this class has just changed.
    ///
    /// <para>
    /// The firmware annotates its less readable numbers with what they mean in human
    /// units, and those annotations are attached to the example's values. Rewriting
    /// <c>kDefaultConfigCheckSeconds</c> from 3600 to this device's 1800 while leaving
    /// "// 1 hour" beside it produces a file that states something false about itself —
    /// and it is a comment, so nothing downstream will ever catch it. Only glosses on
    /// values that actually changed are dropped: a bound the API renders to the same
    /// number keeps its (still correct) note.
    /// </para>
    /// </summary>
    /// <param name="name">The constant whose line to tidy.</param>
    private void DropTrailingGloss(string name)
    {
        // Not routed through Replace(): most numbers carry no gloss, so finding none
        // is the normal case rather than a rename to shout about.
        Regex gloss = new Regex(
            $@"^(constexpr\s+[A-Za-z_]\w*\s+{Regex.Escape(name)}\s*=\s*\d+;)[ \t]*//[^\n]*",
            LineOptions);

        _text = gloss.Replace(_text, match => match.Groups[1].Value, 1);
    }

    /// <summary>
    /// Replaces a whole multi-line C string literal — declaration through its
    /// terminating semicolon — with a new one. Used for the receiver public key,
    /// whose placeholder in the firmware template spans four quoted lines.
    /// </summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="literal">
    /// The replacement literal body: quoted lines, <c>\n</c>-separated, the last one
    /// already carrying the semicolon (as <see cref="ConfigSnippetBuilder"/> renders it).
    /// </param>
    public void SetMultiLineLiteral(string name, string literal)
    {
        // Non-greedy up to the first line-terminating semicolon, so this stops at the
        // end of THIS declaration rather than running on to the next one.
        Replace(
            name,
            new Regex($@"^constexpr\s+char\s+{Regex.Escape(name)}\[\]\s*=[\s\S]*?;[ \t]*$", LineOptions),
            _ => $"constexpr char {name}[] =\n{literal}");
    }

    /// <summary>
    /// Inserts comment lines immediately above a constant's declaration, for the notes
    /// that are per-device rather than per-firmware: the ack key situation and the
    /// fingerprint of the receiver key this file was rendered against.
    /// </summary>
    /// <param name="name">The constant the comment belongs to.</param>
    /// <param name="comment">
    /// The comment text, <c>//</c>-prefixed on every line, <c>\n</c>-separated, with no
    /// trailing newline.
    /// </param>
    public void InsertCommentAbove(string name, string comment)
    {
        // A zero-width look-ahead: the declaration itself is not consumed, so the
        // comment lands above a line this class may also rewrite the value of.
        Replace(
            name,
            new Regex($@"^(?=constexpr\s+[A-Za-z_]\w*\s+{Regex.Escape(name)}\b)", LineOptions),
            _ => comment + "\n");
    }

    /// <summary>
    /// Prepends a banner to the very top of the file. Comments are legal before
    /// <c>#pragma once</c>, so this needs no anchor and cannot fail.
    /// </summary>
    /// <param name="banner">The banner text, with no trailing newline.</param>
    public void PrependBanner(string banner)
    {
        _text = banner + "\n\n" + _text;
    }

    /// <summary>
    /// Removes the file's own <c>// ===</c>-fenced header block when it mentions the
    /// given marker.
    ///
    /// <para>
    /// The firmware's header opens with "Config.example.h - Committed template (NO
    /// secrets)" and tells the reader to <c>cp Config.example.h Config.h</c> and fill
    /// in the WiFi credentials. In the firmware repo that is exactly right; in the file
    /// the dashboard hands over it is an instruction to redo work that has already been
    /// done, sitting a few lines under a banner that says the opposite.
    /// </para>
    ///
    /// <para>
    /// Unlike every other method here this one is <b>cosmetic and does not throw</b>.
    /// It anchors on prose rather than on a constant, and prose gets rewritten — a
    /// comment edit in the firmware must not be able to break provisioning. If the
    /// block stops matching, the worst outcome is a stale paragraph in the output,
    /// which is where this started.
    /// </para>
    /// </summary>
    /// <param name="marker">Text the block must contain to be removed.</param>
    public void RemoveHeaderBlockMentioning(string marker)
    {
        // A whole fenced block: an === rule, the comment lines under it, and the ===
        // rule that closes it. Non-greedy so it stops at the first closing rule.
        Regex block = new Regex(@"^// ={10,}\n(?://[^\n]*\n)*?// ={10,}\n\n?", LineOptions);

        foreach (Match candidate in block.Matches(_text))
        {
            if (candidate.Value.Contains(marker, StringComparison.Ordinal))
            {
                _text = _text.Remove(candidate.Index, candidate.Length);
                return;
            }
        }
    }

    /// <summary>
    /// Applies one anchored rewrite, replacing the first match only.
    /// </summary>
    /// <param name="name">The constant being rewritten, for the failure message.</param>
    /// <param name="pattern">The anchor.</param>
    /// <param name="replacement">
    /// Builds the replacement text. A delegate rather than a <c>$1</c> substitution
    /// string on purpose: a value containing <c>$&amp;</c> or <c>$1</c> would otherwise
    /// be read as a backreference, and a broker URI or an SSID is exactly the sort of
    /// string that carries punctuation nobody thought about.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The constant is not in the template — it was renamed or removed on the firmware
    /// side. See the class summary for why this is fatal rather than ignored.
    /// </exception>
    private void Replace(string name, Regex pattern, MatchEvaluator replacement)
    {
        if (!pattern.IsMatch(_text))
        {
            throw new InvalidOperationException(
                $"The firmware config template has no constant '{name}' in the expected form, so the "
                + "provisioning endpoint cannot fill in this device's value. It was probably renamed or "
                + "retyped in ESP32/src/config/Config.example.h; update ConfigSnippetBuilder to match. "
                + "Rendering the file without it is not an option: it would hand out a tracker that "
                + "builds cleanly and then uses the template's placeholder.");
        }

        _text = pattern.Replace(_text, replacement, 1);
    }

    /// <summary>
    /// Escapes the only two characters a single-line C string literal cannot carry
    /// raw. Newlines are stripped rather than escaped: every value that reaches here
    /// is a topic, an id or a URI, so one arriving with a newline in it is a data
    /// error, and a literal <c>\n</c> in a topic would be a bug the firmware could not
    /// diagnose. Mirrors escapeCString in the browser's configSecrets.ts.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped value, safe to place between quotes.</returns>
    private static string EscapeCString(string value)
    {
        return value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
