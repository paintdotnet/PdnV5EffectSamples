# PdnV5EffectSamples
Sample effect plugins that show how to use the new effect systems available in Paint.NET v5.0.

The new effect system is split into two parts: `BitmapEffect`, which run on the CPU, and `GpuEffect` which use Direct2D for rendering on the GPU.

`BitmapEffect` is the replacement for the "classic" `Effect` base class. It's very similar but but uses virtualized storage for inputs and outputs, permits access to all layers (and a composite of the whole image aka 'Document'), supports output pixel formats other than BGRA32, and has a more modern API (the "classic" effect plugin system dates back to 2004!).

`GpuEffect`, along with `GpuImageEffect` and `GpuDrawingEffect`, use Direct2D for rendering on the GPU. It has the same new features as `BitmapEffect`, except each input (layers, document, and selection mask) are also provided in a GPU-compatible format (`IDeviceImage` instead of `IBitmapSource`).

- `GpuImageEffect` is the most common base class, giving direct access to Direct2D's imaging and effects system. Here you can create an effect graph comprised of [built-in Direct2D effects](https://learn.microsoft.com/en-us/windows/win32/direct2d/built-in-effects), any of the built-in Paint.NET GPU effects, and custom pixel shaders implemented using [ComputeSharp.D2D1](https://github.com/Sergio0694/ComputeSharp).
- `GpuDrawingEffect` provides an `OnDraw(IDeviceContext)` method for you to implement imperative drawing with methods like `DrawRectangle`, `FillGeometry`, etc.
  - This is implemented using `GpuImageEffect`: drawing commands are buffered into a Direct2D command list, which itself is an `IDeviceImage`.
- `GpuEffect` is the lowest-level base class, allowing you to specify the Direct2D drawing commands separately for each rendered tile. This can be used to optimize performance in extreme scenarios where the number of drawing commands is high, and Direct2D's built-in clipping performance is not sufficient. However, this is rarely the case, and `GpuDrawingEffect` should be preferred for when you need to perform imperative drawing.

Microsoft's has documentation for Direct2D effects, although some of it must be gleamed from their open-source Win2D project.
* Effects main page https://docs.microsoft.com/en-us/windows/win32/direct2d/effects-overview
* Built-in effects https://docs.microsoft.com/en-us/windows/win32/direct2d/built-in-effects
* Effect precision and clamping https://microsoft.github.io/Win2D/WinUI2/html/EffectPrecision.htm
