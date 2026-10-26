// Provider path data adapted from qunqin24/Pulse, commit fc10fb7f13999d16f27f283c4bd9e83c80a2885d.
// Source: Sources/Pulse/Resources/{openai,claude,deepseek,zai}.svg
// Modification: SVG path data is rendered as WPF Geometry using its original 24 x 24
// viewBox and even-odd fill rule. No outline coordinates have been redesigned.
// Upstream attribution:
// https://github.com/qunqin24/Pulse/blob/fc10fb7f13999d16f27f283c4bd9e83c80a2885d/THIRD_PARTY_NOTICES.md
// These marks are from Lobe Icons (@lobehub/icons-static-svg 1.95.1).
// Names and marks remain the property of their respective owners and identify
// compatible services; their inclusion does not imply endorsement.
//
// MIT License
//
// Copyright (c) 2023 LobeHub
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
namespace Pulse.Desktop;

/// <summary>Draws the provider marks used by the upstream Pulse interface.</summary>
public static class ProviderMark
{
    // https://github.com/qunqin24/Pulse/blob/fc10fb7f13999d16f27f283c4bd9e83c80a2885d/Sources/Pulse/Resources/openai.svg
    private static readonly Geometry OpenAi = Parse(
        "F0 M 9.205 8.658 v -2.26 c 0 -.19 .072 -.333 .238 -.428 l 4.543 -2.616 c .619 -.357 1.356 -.523 2.117 -.523 2.854 0 4.662 2.212 4.662 4.566 0 .167 0 .357 -.024 .547 l -4.71 -2.759 a .797 .797 0 0 0 -.856 0 l -5.97 3.473 z m 10.609 8.8 V 12.06 c 0 -.333 -.143 -.57 -.429 -.737 l -5.97 -3.473 1.95 -1.118 a .433 .433 0 0 1 .476 0 l 4.543 2.617 c 1.309 .76 2.189 2.378 2.189 3.948 0 1.808 -1.07 3.473 -2.76 4.163 z M 7.802 12.703 l -1.95 -1.142 c -.167 -.095 -.239 -.238 -.239 -.428 V 5.899 c 0 -2.545 1.95 -4.472 4.591 -4.472 1 0 1.927 .333 2.712 .928 L 8.23 5.067 c -.285 .166 -.428 .404 -.428 .737 v 6.898 z M 12 15.128 l -2.795 -1.57 v -3.33 L 12 8.658 l 2.795 1.57 v 3.33 L 12 15.128 z m 1.796 7.23 c -1 0 -1.927 -.332 -2.712 -.927 l 4.686 -2.712 c .285 -.166 .428 -.404 .428 -.737 v -6.898 l 1.974 1.142 c .167 .095 .238 .238 .238 .428 v 5.233 c 0 2.545 -1.974 4.472 -4.614 4.472 z m -5.637 -5.303 l -4.544 -2.617 c -1.308 -.761 -2.188 -2.378 -2.188 -3.948 A 4.482 4.482 0 0 1 4.21 6.327 v 5.423 c 0 .333 .143 .571 .428 .738 l 5.947 3.449 -1.95 1.118 a .432 .432 0 0 1 -.476 0 z m -.262 3.9 c -2.688 0 -4.662 -2.021 -4.662 -4.519 0 -.19 .024 -.38 .047 -.57 l 4.686 2.71 c .286 .167 .571 .167 .856 0 l 5.97 -3.448 v 2.26 c 0 .19 -.07 .333 -.237 .428 l -4.543 2.616 c -.619 .357 -1.356 .523 -2.117 .523 z m 5.899 2.83 a 5.947 5.947 0 0 0 5.827 -4.756 C 22.287 18.339 24 15.84 24 13.296 c 0 -1.665 -.713 -3.282 -1.998 -4.448 .119 -.5 .19 -.999 .19 -1.498 0 -3.401 -2.759 -5.947 -5.946 -5.947 -.642 0 -1.26 .095 -1.88 .31 A 5.962 5.962 0 0 0 10.205 0 a 5.947 5.947 0 0 0 -5.827 4.757 C 1.713 5.447 0 7.945 0 10.49 c 0 1.666 .713 3.283 1.998 4.448 -.119 .5 -.19 1 -.19 1.499 0 3.401 2.759 5.946 5.946 5.946 .642 0 1.26 -.095 1.88 -.309 a 5.96 5.96 0 0 0 4.162 1.713 z");

    // https://github.com/qunqin24/Pulse/blob/fc10fb7f13999d16f27f283c4bd9e83c80a2885d/Sources/Pulse/Resources/claude.svg
    private static readonly Geometry Claude = Parse(
        "F0 M 4.709 15.955 l 4.72 -2.647 .08 -.23 -.08 -.128 H 9.2 l -.79 -.048 -2.698 -.073 -2.339 -.097 -2.266 -.122 -.571 -.121 L 0 11.784 l .055 -.352 .48 -.321 .686 .06 1.52 .103 2.278 .158 1.652 .097 2.449 .255 h .389 l .055 -.157 -.134 -.098 -.103 -.097 -2.358 -1.596 -2.552 -1.688 -1.336 -.972 -.724 -.491 -.364 -.462 -.158 -1.008 .656 -.722 .881 .06 .225 .061 .893 .686 1.908 1.476 2.491 1.833 .365 .304 .145 -.103 .019 -.073 -.164 -.274 -1.355 -2.446 -1.446 -2.49 -.644 -1.032 -.17 -.619 a 2.97 2.97 0 0 1 -.104 -.729 L 6.283 .134 6.696 0 l .996 .134 .42 .364 .62 1.414 1.002 2.229 1.555 3.03 .456 .898 .243 .832 .091 .255 h .158 V 9.01 l .128 -1.706 .237 -2.095 .23 -2.695 .08 -.76 .376 -.91 .747 -.492 .584 .28 .48 .685 -.067 .444 -.286 1.851 -.559 2.903 -.364 1.942 h .212 l .243 -.242 .985 -1.306 1.652 -2.064 .73 -.82 .85 -.904 .547 -.431 h 1.033 l .76 1.129 -.34 1.166 -1.064 1.347 -.881 1.142 -1.264 1.7 -.79 1.36 .073 .11 .188 -.02 2.856 -.606 1.543 -.28 1.841 -.315 .833 .388 .091 .395 -.328 .807 -1.969 .486 -2.309 .462 -3.439 .813 -.042 .03 .049 .061 1.549 .146 .662 .036 h 1.622 l 3.02 .225 .79 .522 .474 .638 -.079 .485 -1.215 .62 -1.64 -.389 -3.829 -.91 -1.312 -.329 h -.182 v .11 l 1.093 1.068 2.006 1.81 2.509 2.33 .127 .578 -.322 .455 -.34 -.049 -2.205 -1.657 -.851 -.747 -1.926 -1.62 h -.128 v .17 l .444 .649 2.345 3.521 .122 1.08 -.17 .353 -.608 .213 -.668 -.122 -1.374 -1.925 -1.415 -2.167 -1.143 -1.943 -.14 .08 -.674 7.254 -.316 .37 -.729 .28 -.607 -.461 -.322 -.747 .322 -1.476 .389 -1.924 .315 -1.53 .286 -1.9 .17 -.632 -.012 -.042 -.14 .018 -1.434 1.967 -2.18 2.945 -1.726 1.845 -.414 .164 -.717 -.37 .067 -.662 .401 -.589 2.388 -3.036 1.44 -1.882 .93 -1.086 -.006 -.158 h -.055 L 4.132 18.56 l -1.13 .146 -.487 -.456 .061 -.746 .231 -.243 1.908 -1.312 -.006 .006 z");

    // https://github.com/qunqin24/Pulse/blob/fc10fb7f13999d16f27f283c4bd9e83c80a2885d/Sources/Pulse/Resources/deepseek.svg
    private static readonly Geometry DeepSeek = Parse(
        "F0 M 23.748 4.482 c -.254 -.124 -.364 .113 -.512 .234 -.051 .039 -.094 .09 -.137 .136 -.372 .397 -.806 .657 -1.373 .626 -.829 -.046 -1.537 .214 -2.163 .848 -.133 -.782 -.575 -1.248 -1.247 -1.548 -.352 -.156 -.708 -.311 -.955 -.65 -.172 -.241 -.219 -.51 -.305 -.774 -.055 -.16 -.11 -.323 -.293 -.35 -.2 -.031 -.278 .136 -.356 .276 -.313 .572 -.434 1.202 -.422 1.84 .027 1.436 .633 2.58 1.838 3.393 .137 .093 .172 .187 .129 .323 -.082 .28 -.18 .552 -.266 .833 -.055 .179 -.137 .217 -.329 .14 a 5.526 5.526 0 0 1 -1.736 -1.18 c -.857 -.828 -1.631 -1.742 -2.597 -2.458 a 11.365 11.365 0 0 0 -.689 -.471 c -.985 -.957 .13 -1.743 .388 -1.836 .27 -.098 .093 -.432 -.779 -.428 -.872 .004 -1.67 .295 -2.687 .684 a 3.055 3.055 0 0 1 -.465 .137 9.597 9.597 0 0 0 -2.883 -.102 c -1.885 .21 -3.39 1.102 -4.497 2.623 C .082 8.606 -.231 10.684 .152 12.85 c .403 2.284 1.569 4.175 3.36 5.653 1.858 1.533 3.997 2.284 6.438 2.14 1.482 -.085 3.133 -.284 4.994 -1.86 .47 .234 .962 .327 1.78 .397 .63 .059 1.236 -.03 1.705 -.128 .735 -.156 .684 -.837 .419 -.961 -2.155 -1.004 -1.682 -.595 -2.113 -.926 1.096 -1.296 2.746 -2.642 3.392 -7.003 .05 -.347 .007 -.565 0 -.845 -.004 -.17 .035 -.237 .23 -.256 a 4.173 4.173 0 0 0 1.545 -.475 c 1.396 -.763 1.96 -2.015 2.093 -3.517 .02 -.23 -.004 -.467 -.247 -.588 z M 11.581 18 c -2.089 -1.642 -3.102 -2.183 -3.52 -2.16 -.392 .024 -.321 .471 -.235 .763 .09 .288 .207 .486 .371 .739 .114 .167 .192 .416 -.113 .603 -.673 .416 -1.842 -.14 -1.897 -.167 -1.361 -.802 -2.5 -1.86 -3.301 -3.307 -.774 -1.393 -1.224 -2.887 -1.298 -4.482 -.02 -.386 .093 -.522 .477 -.592 a 4.696 4.696 0 0 1 1.529 -.039 c 2.132 .312 3.946 1.265 5.468 2.774 .868 .86 1.525 1.887 2.202 2.891 .72 1.066 1.494 2.082 2.48 2.914 .348 .292 .625 .514 .891 .677 -.802 .09 -2.14 .11 -3.054 -.614 z m 1 -6.44 a .306 .306 0 0 1 .415 -.287 .302 .302 0 0 1 .2 .288 .306 .306 0 0 1 -.31 .307 .303 .303 0 0 1 -.304 -.308 z m 3.11 1.596 c -.2 .081 -.399 .151 -.59 .16 a 1.245 1.245 0 0 1 -.798 -.254 c -.274 -.23 -.47 -.358 -.552 -.758 a 1.73 1.73 0 0 1 .016 -.588 c .07 -.327 -.008 -.537 -.239 -.727 -.187 -.156 -.426 -.199 -.688 -.199 a .559 .559 0 0 1 -.254 -.078 c -.11 -.054 -.2 -.19 -.114 -.358 .028 -.054 .16 -.186 .192 -.21 .356 -.202 .767 -.136 1.146 .016 .352 .144 .618 .408 1.001 .782 .391 .451 .462 .576 .685 .914 .176 .265 .336 .537 .445 .848 .067 .195 -.019 .354 -.25 .452 z");

    // https://github.com/qunqin24/Pulse/blob/fc10fb7f13999d16f27f283c4bd9e83c80a2885d/Sources/Pulse/Resources/zai.svg
    private static readonly Geometry Zai = Parse(
        "F0 M 12.105 2 L 9.927 4.953 H .653 L 2.83 2 h 9.276 z M 23.254 19.048 L 21.078 22 h -9.242 l 2.174 -2.952 h 9.244 z M 24 2 L 9.264 22 H 0 L 14.736 2 H 24 z");

    /// <summary>Draws a known provider mark; returns false when the provider has no bundled mark.</summary>
    public static bool IsKnown(string providerId)=>Find(providerId) is not null;

    public static bool Draw(DrawingContext drawing, string providerId, Rect bounds, Brush brush)
    {
        var geometry = Find(providerId);
        if (geometry is null) return false;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)) return true;

        var side = Math.Min(bounds.Width, bounds.Height);
        drawing.PushTransform(new MatrixTransform(
            side / 24, 0, 0, side / 24,
            bounds.X + (bounds.Width - side) / 2,
            bounds.Y + (bounds.Height - side) / 2));
        drawing.DrawGeometry(brush, null, geometry);
        drawing.Pop();
        return true;
    }

    /// <summary>Creates a fixed-size mark for a card header. Unknown providers render an empty element.</summary>
    public static FrameworkElement Create(string providerId, double size, Brush? brush = null) =>
        new MarkElement(providerId, brush ?? Ui.Text)
        {
            Width = size,
            Height = size,
            IsHitTestVisible = false
        };

    private static Geometry? Find(string providerId) => providerId switch
    {
        "codex" => OpenAi,
        "claudeCode" => Claude,
        "deepSeek" => DeepSeek,
        "zai" => Zai,
        _ => null
    };

    private static Geometry Parse(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    private sealed class MarkElement(string providerId, Brush brush) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing) =>
            Draw(drawing, providerId, new Rect(RenderSize), brush);
    }
}

