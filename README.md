![image](https://user-images.githubusercontent.com/6692947/150184680-1ae82d62-891e-4dbd-b52b-e975c57f9761.png)


What is TesseractOCR
=========

It is a .NET wrapper for Tesseract 5.0.0 that is originally copied from Charles Weld (https://github.com/charlesw/tesseract) and modified for my own needs

How to use
============

You need trained data in tessdata by language
You can get them at https://github.com/tesseract-ocr/tessdata or https://github.com/tesseract-ocr/tessdata_fast

```c#
using (var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default))
{
     using (var img = Pix.Image.LoadFromFile(testImagePath))
     {
         using (var page = engine.Process(img))
         {
             Console.WriteLine("Mean confidence: {0}", page.MeanConfidence);
             Console.WriteLine("Text (GetText): \r\n{0}", page.Text);
             Console.WriteLine("Text (iterator):");
         }
    }
}
```

For more examples see https://github.com/Sicos1977/TesseractOCR/wiki/examples.md

Supported input formats
=======================

Tesseract uses the Leptonica library to read images with one of these formats:

- PNG - requires libpng, libz
- JPEG - requires libjpeg / libjpeg-turbo
- TIFF - requires libtiff, libz
- JPEG 2000 - requires libopenjp2
- GIF - requires libgif (giflib)
- WebP (including animated WebP) - requires libwebp
- BMP - no library required*
= PNM - no library required*
* Except Leptonica

## I have dropped support for the Windows.Drawing.Image namespace since this only works good on Windows an not on other systems. You should be fine with Leptonica

Installing via NuGet
====================

The easiest way to install TesseractOCR is via NuGet.

In Visual Studio's Package Manager Console, simply enter the following command:

    Install-Package TesseractOCR


## License Information

* Copyright 2012-2019 Charles Weld (https://github.com/charlesw)
* Copyright 2021-2022 Kees van Spelde

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

Core Team
=========
* [Sicos1977](https://github.com/sicos1977) (Kees van Spelde)
* [charlesw](https://github.com/charlesw) (Charles Weld) - Copied repro from him
