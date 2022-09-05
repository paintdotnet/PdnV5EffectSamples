# PdnGpuEffectSamples
Sample effect plugins that show how to use the new Direct2D/GPU effects system available in Paint.NET v5.0

In Paint.NET v5.0, effect plugins will now have access to Direct2D for rendering, which makes it possible to use the GPU. Direct2D has a rich effects system of its own. Effects can be combined together in a transform graph, either in a simple linear fashion (A's output is B's input, and then B's input is C's output, etc), or in a more complicated manner (A's output is B and C's input, then D's inputs are A's output and C's output, etc.).

Custom Direct2D effects can be implemented, which can have their own internal transform graph comprised of other effects, or of lower level "transforms". Transforms have access to lower-level primitives, and can utilize HLSL pixel shaders.

Microsoft's has documentation for Direct2D effects, although some of it must be gleamed from their open-source Win2D project.
* Effects main page https://docs.microsoft.com/en-us/windows/win32/direct2d/effects-overview
* Built-in effects https://docs.microsoft.com/en-us/windows/win32/direct2d/built-in-effects
* Effect precision and clamping https://microsoft.github.io/Win2D/WinUI2/html/EffectPrecision.htm

Paint.NET v4.4 is still in early development, and does not yet have a public alpha release.
