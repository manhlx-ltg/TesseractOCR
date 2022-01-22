﻿//
// Page.cs
//
// Author: Kees van Spelde <sicos2002@hotmail.com>
//
// Copyright 2012-2019 Charles Weld
// Copyright 2021-2022 Kees van Spelde
//
// Licensed under the Apache License, Version 2.0 (the "License");
//
// - You may not use this file except in compliance with the License.
// - You may obtain a copy of the License at
//
//   http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TesseractOCR.Exceptions;
using TesseractOCR.Enums;
using TesseractOCR.Internal;
using TesseractOCR.Interop;
using TesseractOCR.Iterators;
using TesseractOCR.Loggers;

// ReSharper disable UnusedMember.Global

namespace TesseractOCR.Elements
{
    public sealed class Page : DisposableBase
    {
        #region Consts
        /// <summary>
        ///     XHTML begin Tag
        /// </summary>
        public const string XhtmlBeginTag =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\"\n"
            + "    \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"en\" "
            + "lang=\"en\">\n <head>\n  <title></title>\n"
            + "<meta http-equiv=\"Content-Type\" content=\"text/html;"
            + "charset=utf-8\" />\n"
            + "  <meta name='ocr-system' content='tesseract' />\n"
            + "  <meta name='ocr-capabilities' content='ocr_page ocr_carea ocr_par"
            + " ocr_line ocrx_word"
            + "'/>\n"
            + "</head>\n<body>\n";

        /// <summary>
        ///     XHTML end Tag
        /// </summary>
        public const string XhtmlEndTag = " </body>\n</html>\n";

        /// <summary>
        ///     HTML begin tag
        /// </summary>
        public const string HtmlBeginTag =
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\""
            + " \"http://www.w3.org/TR/html4/loose.dtd\">\n"
            + "<html>\n<head>\n<title></title>\n"
            + "<meta http-equiv=\"Content-Type\" content=\"text/html;"
            + "charset=utf-8\" />\n<meta name='ocr-system' content='tesseract'/>\n"
            + "</head>\n<body>\n";

        /// <summary>
        ///     HTML end tag
        /// </summary>
        public const string HtmlEndTag = "</body>\n</html>\n";
        #endregion

        #region Fields
        private bool _runRecognitionPhase;
        private Rect _regionOfInterest;
        #endregion

        #region Properties
        /// <summary>
        ///     <see cref="TesseractEngine"/>
        /// </summary>
        public TesseractEngine Engine { get; }

        /// <summary>
        ///     Returns the <see cref="Pix"/> <see cref="Image" /> that is being ocr'd
        /// </summary>
        public Pix.Image Image { get; }

        /// <summary>
        ///     Returns the name of the image being ocr'd
        /// </summary>
        /// <remarks>
        ///     This is also used for some of the more advanced functionality such as identifying the associated UZN file if
        ///     present
        /// </remarks>
        public string ImageName { get; }

        /// <summary>
        ///     Returns the current region of interest being parsed
        /// </summary>
        public Rect RegionOfInterest
        {
            get => _regionOfInterest;
            set
            {
                if (value.X1 < 0 || value.Y1 < 0 || value.X2 > Image.Width || value.Y2 > Image.Height)
                    throw new ArgumentException(
                        "The region of interest to be processed must be within the image bounds", nameof(value));

                if (_regionOfInterest == value) return;
                _regionOfInterest = value;

                // update region of interest in image
                TessApi.Native.BaseApiSetRectangle(Engine.Handle, _regionOfInterest.X1, _regionOfInterest.Y1,
                    _regionOfInterest.Width, _regionOfInterest.Height);

                // Request rerun of recognition on the next call that requires recognition
                _runRecognitionPhase = false;
            }
        }

        /// <summary>
        ///     Returns the segmentation mode used to OCR the specified image
        /// </summary>
        public PageSegMode SegmentMode { get; }

        /// <summary>
        ///     Returns the page number
        /// </summary>
        public int Number { get; internal set; }

        /// <summary>
        ///     Returns the thresholded image that was OCR'd
        /// </summary>
        /// <returns></returns>
        public Pix.Image ThresholdedImage
        {
            get
            {
                Recognize();

                var pixHandle = TessApi.Native.BaseApiGetThresholdedImage(Engine.Handle);
                if (pixHandle == IntPtr.Zero) throw new TesseractException("Failed to get thresholded image");

                return Pix.Image.Create(pixHandle);
            }
        }

        /// <summary>
        ///     Returns a <see cref="Page" /> object that is used to iterate over the page's layout as defined by the
        ///     current <see cref="RegionOfInterest" />.
        /// </summary>
        /// <returns></returns>
        public Iterators.Page Layout
        {
            get
            {
                Guard.Verify(SegmentMode != PageSegMode.OsdOnly,
                    "Cannot analyse image layout when using OSD only page segmentation, please use DetectBestOrientation instead");

                var resultIteratorHandle = TessApi.Native.BaseApiAnalyseLayout(Engine.Handle);
                return new Iterators.Page(this, resultIteratorHandle);
            }
        }

        /// <summary>
        ///     Returns a <see cref="ResultIterator" /> object that is used to iterate over the page as defined by the current
        ///     <see cref="RegionOfInterest" />
        /// </summary>
        /// <returns></returns>
        public Result ResultIterator
        {
            get
            {
                Recognize();
                var resultIteratorHandle = TessApi.Native.BaseApiGetIterator(Engine.Handle);
                return new Result(this, resultIteratorHandle);
            }
        }

        /// <summary>
        ///     Returns the page's content as plain text
        /// </summary>
        /// <returns>The OCR'd output as text string</returns>
        public string Text
        {
            get
            {
                Recognize();
                return TessApi.BaseApiGetUTF8Text(Engine.Handle);
            }
        }

        /// <summary>
        ///     Returns the page's content as HOCR text
        /// </summary>
        /// <param name="useXHtml">True to use XHTML Output, False to HTML Output</param>
        /// <returns>The OCR'd output as an HOCR text string</returns>
        public string HOcrText(bool useXHtml = false)
        {
            Recognize();

            var result = TessApi.BaseApiGetHOcrText(Engine.Handle, Number);

            return useXHtml
                ? XhtmlBeginTag + result + XhtmlEndTag
                : HtmlBeginTag + result + HtmlEndTag;
        }

        /// <summary>
        ///     Return the page's content as an Alto text
        /// </summary>
        /// <returns>The OCR'd output as an Alto text string.</returns>
        public string AltoText
        {
            get
            {
                Recognize();
                return TessApi.BaseApiGetAltoText(Engine.Handle, Number);
            }
        }

        /// <summary>
        ///     Return the page's content as TSV text.
        /// </summary>
        /// <returns>The OCR'd output as a Tsv text string</returns>
        public string TsvText
        {
            get
            {
                Recognize();
                return TessApi.BaseApiGetTsvText(Engine.Handle, Number);
            }
        }

        /// <summary>
        ///     Returns the page's content as box text
        /// </summary>
        /// <returns>The OCR'd output as a box text string</returns>
        public string BoxText
        {
            get
            {
                Recognize();
                return TessApi.BaseApiGetBoxText(Engine.Handle, Number);
            }
        }

        /// <summary>
        ///     Returns the page's content as LSTM box text
        /// </summary>
        /// <returns>The OCR'd output as a LSTMBox text string</returns>
        public string LstmBoxText
        {
            get
            {
                Recognize();
                return TessApi.BaseApiGetLSTMBoxText(Engine.Handle, Number);
            }
        }

        /// <summary>
        ///     Returns the page's content as a word string box text
        /// </summary>
        /// <returns>The OCR'd output as a WordStrBox text string</returns>
        public string WordStrBoxText
        {
            get
            {
                Recognize();
                return TessApi.BaseApiGetWordStrBoxText(Engine.Handle, Number);
            }
        }

        /// <summary>
        ///     Returns the page's content as UNLV text
        /// </summary>
        /// <returns>The OCR'd output as an UNLV text string</returns>
        public string UnlvText
        {
            get
            {
                Recognize();
                return TessApi.BaseApiGetUnlvText(Engine.Handle);
            }
        }

        /// <summary>
        ///     Returns the mean confidence as a percentage of the recognized text
        /// </summary>
        /// <returns></returns>
        public float MeanConfidence
        {
            get
            {
                Recognize();
                return TessApi.Native.BaseApiMeanTextConf(Engine.Handle) / 100.0f;
            }
        }
        #endregion

        #region Constructor
        /// <summary>
        ///     Creates the <see cref="Page"/> object
        /// </summary>
        /// <param name="engine"><see cref="TesseractEngine"/></param>
        /// <param name="image">The <see cref="Pix"/> <see cref="Image" /> that is being ocr'd</param>
        /// <param name="imageName">The name of the image being ocr'd</param>
        /// <param name="regionOfInterest">The current region of interest being parsed</param>
        /// <param name="segmentMode">The segmentation mode used to OCR the specified image</param>
        /// <param name="number">The page number</param>
        internal Page(
            TesseractEngine engine,
            Pix.Image image,
            string imageName,
            Rect regionOfInterest,
            PageSegMode segmentMode,
            int number)
        {
            Engine = engine;
            Image = image;
            ImageName = imageName;
            RegionOfInterest = regionOfInterest;
            SegmentMode = segmentMode;
            Number = number;
        }
        #endregion

        #region GetSegmentedRegions
        /// <summary>
        ///     Get segmented regions at specified page iterator level
        /// </summary>
        /// <param name="pageIteratorLevel">PageIteratorLevel enum</param>
        /// <returns></returns>
        public List<Rectangle> GetSegmentedRegions(PageIteratorLevel pageIteratorLevel)
        {
            var boxArray = TessApi.Native.BaseApiGetComponentImages(Engine.Handle, pageIteratorLevel, Constants.True, IntPtr.Zero, IntPtr.Zero);
            var boxCount = LeptonicaApi.Native.boxaGetCount(new HandleRef(this, boxArray));
            var boxList = new List<Rectangle>();

            for (var i = 0; i < boxCount; i++)
            {
                var box = LeptonicaApi.Native.boxaGetBox(new HandleRef(this, boxArray), i, PixArrayAccessType.Clone);

                if (box == IntPtr.Zero)
                    continue;

                LeptonicaApi.Native.boxGetGeometry(new HandleRef(this, box), out var px, out var py, out var pw, out var ph);
                boxList.Add(new Rectangle(px, py, pw, ph));
                LeptonicaApi.Native.boxDestroy(ref box);
            }

            LeptonicaApi.Native.boxaDestroy(ref boxArray);

            return boxList;
        }
        #endregion

        #region DetectBestOrientation
        /// <summary>
        ///     Detects the page orientation, with corresponding confidence when using <see cref="PageSegMode.OsdOnly" />
        /// </summary>
        /// <remarks>
        ///     If using full page segmentation mode (i.e. AutoOsd) then consider using <see cref="Layout" /> instead as
        ///     this also provides a deskew angle which isn't available when just performing orientation detection.
        /// </remarks>
        /// <param name="orientation">The detected clockwise page rotation in degrees (0, 90, 180, or 270)</param>
        /// <param name="confidence">The confidence level of the orientation (15 is reasonably confident)</param>
        public void DetectBestOrientation(out int orientation, out float confidence)
        {
            DetectBestOrientationAndScript(out orientation, out confidence, out _, out _);
        }
        #endregion

        #region DetectBestOrientationAndScript
        /// <summary>
        ///     Detects the page orientation, with corresponding confidence when using <see cref="PageSegMode.OsdOnly" />.
        /// </summary>
        /// <remarks>
        ///     If using full page segmentation mode (i.e. AutoOsd) then consider using <see cref="Layout" /> instead as
        ///     this also provides a
        ///     deskew angle which isn't available when just performing orientation detection.
        /// </remarks>
        /// <param name="orientation">The detected clockwise page rotation in degrees (0, 90, 180, or 270).</param>
        /// <param name="confidence">The confidence level of the orientation (15 is reasonably confident).</param>
        /// <param name="scriptName">The name of the script (e.g. Latin)</param>
        /// <param name="scriptConfidence">The confidence level in the script</param>
        public void DetectBestOrientationAndScript(out int orientation, out float confidence, out string scriptName,
            out float scriptConfidence)
        {
            if (TessApi.Native.TessBaseAPIDetectOrientationScript(Engine.Handle, out var orientDeg, out var orientConf,
                    out var scriptNameHandle, out var scriptConf) != 0)
            {
                orientation = orientDeg;
                confidence = orientConf;
                scriptName = scriptNameHandle != IntPtr.Zero ? MarshalHelper.PtrToString(scriptNameHandle, Encoding.ASCII) : null;
                scriptConfidence = scriptConf;
            }
            else
                throw new TesseractException("Failed to detect image orientation.");
        }
        #endregion

        #region Recognize
        internal void Recognize()
        {
            Guard.Verify(SegmentMode != PageSegMode.OsdOnly, "Cannot OCR image when using OSD only page segmentation, please use DetectBestOrientation instead");

            if (_runRecognitionPhase)
                return;

            if (TessApi.Native.BaseApiRecognize(Engine.Handle, new HandleRef(this, IntPtr.Zero)) != 0)
                throw new InvalidOperationException("Recognition of image failed");

            _runRecognitionPhase = true;

            // Now write out the thresholded image if required to do so
            if (!Engine.TryGetBoolVariable("tessedit_write_images", out var tesseditWriteImages) || !tesseditWriteImages)
                return;

            using (ThresholdedImage)
            {
                var filePath = Path.Combine(Environment.CurrentDirectory, "tessinput.tif");
                try
                {
                    ThresholdedImage.Save(filePath, ImageFormat.TiffG4);
                    Logger.LogInformation($"Successfully saved the thresholded image to '{filePath}'");
                }
                catch (Exception error)
                {
                    Logger.LogError($"Failed to save the thresholded image to '{filePath}', error: {error}");
                }
            }
        }
        #endregion

        #region Dispose
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                TessApi.Native.BaseApiClear(Engine.Handle);
        }
        #endregion
    }
}