﻿namespace PptxTemplater
{
  using System;
  using System.Collections.Generic;
  using System.Drawing;
  using System.Drawing.Imaging;
  using System.IO;
  using System.Linq;
  using System.Text.RegularExpressions;
  using DocumentFormat.OpenXml;
  using DocumentFormat.OpenXml.Packaging;
  using DocumentFormat.OpenXml.Presentation;

  /// <summary>
  /// Represents a PowerPoint file.
  /// </summary>
  /// <returns>Follows the facade pattern.</returns>
  public sealed class Pptx : IDisposable
  {
    private readonly PresentationDocument _presentationDocument;

    /// <summary>
    /// Regex pattern to extract tags from templates.
    /// </summary>
    public static readonly Regex TagPattern = new Regex(@"{{[A-Za-z0-9_+\-\.]*}}");

    /// <summary>
    /// MIME type for PowerPoint pptx files.
    /// </summary>
    public const string MimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    #region ctor

    /// <summary>
    /// Initializes a new instance of the <see cref="Pptx"/> class.
    /// </summary>
    /// <param name="file">The PowerPoint file.</param>
    /// <param name="access">Access mode to use to open the PowerPoint file.</param>
    /// <remarks>Opens a PowerPoint file in read-write (default) or read only mode.</remarks>
    public Pptx(string file, FileAccess access = FileAccess.ReadWrite)
    {
      bool isEditable = false;
      switch (access)
      {
        case FileAccess.Read:
          isEditable = false;
          break;
        case FileAccess.Write:
        case FileAccess.ReadWrite:
          isEditable = true;
          break;
      }

      this._presentationDocument = PresentationDocument.Open(file, isEditable);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pptx"/> class.
    /// </summary>
    /// <param name="stream">The PowerPoint stream.</param>
    /// <param name="access">Access mode to use to open the PowerPoint file.</param>
    /// <remarks>Opens a PowerPoint stream in read-write (default) or read only mode.</remarks>
    public Pptx(Stream stream, FileAccess access = FileAccess.ReadWrite)
    {
      bool isEditable = false;
      switch (access)
      {
        case FileAccess.Read:
          isEditable = false;
          break;
        case FileAccess.Write:
        case FileAccess.ReadWrite:
          isEditable = true;
          break;
      }

      this._presentationDocument = PresentationDocument.Open(stream, isEditable);
    }

    #endregion ctor

    public void Dispose()
    {
      this.Close();
    }

    /// <summary>
    /// Closes the PowerPoint file.
    /// </summary>
    /// <remarks>
    /// 99% of the time this is not needed, the PowerPoint file will get closed when the destructor is being called.
    /// </remarks>
    public void Close()
    {
      this._presentationDocument.Save();
      this._presentationDocument.Dispose();
    }

    /// <summary>
    /// Counts the number of slides.
    /// </summary>
    /// <returns>The number of slides.</returns>
    /// <remarks>
    /// <see href="http://msdn.microsoft.com/en-us/library/office/gg278331">How to: Get All the Text in All Slides in a Presentation</see>
    /// </remarks>
    public int SlidesCount()
    {
      PresentationPart presentationPart = this._presentationDocument.PresentationPart;
      if (presentationPart != null) return presentationPart.SlideParts.Count();
      return 0;
    }

    /// <summary>
    /// Finds the slides matching a given note.
    /// </summary>
    /// <param name="note">Note to match the slide with.</param>
    /// <returns>The matching slides.</returns>
    public IEnumerable<PptxSlide> FindSlidesByNote(string note)
    {
      List<PptxSlide> slides = new List<PptxSlide>();

      for (int i = 0; i < this.SlidesCount(); i++)
      {
        PptxSlide slide = this.GetSlide(i);

        IEnumerable<string> notes = slide.GetNotes();

        if (notes.Any(n => n.Contains(note)))
        {
          slides.Add(slide);
        }
      }

      return slides;
    }

    /// <summary>
    /// Gets the thumbnail (PNG format) associated with the PowerPoint file.
    /// </summary>
    /// <param name="size">The size of the thumbnail to generate, default is 256x192 pixels in 4:3 (160x256 in 16:10 portrait).</param>
    /// <returns>The thumbnail as a byte array (PNG format).</returns>
    /// <remarks>
    /// Even if the PowerPoint file does not contain any slide, still a thumbnail is generated.
    /// If the given size is bigger than the default size then the thumbnail is upscaled and looks blurry so don't do it.
    /// </remarks>
    public byte[] GetThumbnail(Size size = default(Size))
    {
      byte[] thumbnail;

      var thumbnailPart = this._presentationDocument.ThumbnailPart;

      using (var stream = thumbnailPart.GetStream(FileMode.Open, FileAccess.Read))
      {
        var image = Image.FromStream(stream);
        if (size != default(Size))
        {
          image = image.GetThumbnailImage(size.Width, size.Height, null, IntPtr.Zero);
        }

        using (var memoryStream = new MemoryStream())
        {
          image.Save(memoryStream, ImageFormat.Png);
          thumbnail = memoryStream.ToArray();
        }
      }

      return thumbnail;
    }

    /// <summary>
    /// Gets all the slides inside PowerPoint file.
    /// </summary>
    /// <returns>All the slides.</returns>
    public IEnumerable<PptxSlide> GetSlides()
    {
      List<PptxSlide> slides = new List<PptxSlide>();


      for (int i = 0; i < SlidesCount(); i++)
      {
        slides.Add(this.GetSlide(i));
      }

      return slides;
    }

    /// <summary>
    /// Gets the PptxSlide given a slide index.
    /// </summary>
    /// <param name="slideIndex">Index of the slide.</param>
    /// <returns>A PptxSlide.</returns>
    public PptxSlide GetSlide(int slideIndex)
    {
      PresentationPart presentationPart = this._presentationDocument.PresentationPart;

      // Get the collection of slide IDs
      OpenXmlElementList slideIds = presentationPart.Presentation.SlideIdList.ChildElements;

      // Get the relationship ID of the slide
      string relId = ((SlideId)slideIds[slideIndex]).RelationshipId;

      // Get the specified slide part from the relationship ID
      SlidePart slidePart = (SlidePart)presentationPart.GetPartById(relId);

      return new PptxSlide(presentationPart, slidePart);
    }

    /// <summary>
    /// Replaces the cells from a table (tbl).
    /// Algorithm for a slide template containing one table.
    /// </summary>
    public static IEnumerable<PptxSlide> ReplaceTable_One(PptxSlide slideTemplate, PptxTable tableTemplate,
      IList<PptxTable.Cell[]> rows)
    {
      return ReplaceTable_Multiple(slideTemplate, tableTemplate, rows, new List<PptxSlide>());
    }

    /// <summary>
    /// Replaces the cells from a table (tbl).
    /// Algorithm for a slide template containing multiple tables.
    /// </summary>
    /// <param name="slideTemplate">The slide template that contains the table(s).</param>
    /// <param name="tableTemplate">The table (tbl) to use, should be inside the slide template.</param>
    /// <param name="rows">The rows to replace the table's cells.</param>
    /// <param name="existingSlides">Existing slides created for the other tables inside the slide template.</param>
    /// <returns>The newly created slides if any.</returns>
    public static IEnumerable<PptxSlide> ReplaceTable_Multiple(PptxSlide slideTemplate, PptxTable tableTemplate,
      IList<PptxTable.Cell[]> rows, List<PptxSlide> existingSlides)
    {
      List<PptxSlide> slidesCreated = new List<PptxSlide>();

      string tag = tableTemplate.Title;

      PptxSlide lastSlide = slideTemplate;
      if (existingSlides.Count > 0)
      {
        lastSlide = existingSlides.Last();
      }

      PptxSlide lastSlideTemplate = lastSlide.Clone();

      foreach (PptxSlide slide in existingSlides)
      {
        PptxTable table = slide.FindTables(tag).First();
        List<PptxTable.Cell[]> remainingRows = table.SetRows(rows);
        rows = remainingRows;
      }

      // Force SetRows() at least once if there is no existingSlides
      // this means we are being called by ReplaceTable_One()
      bool loopOnce = existingSlides.Count == 0;

      while (loopOnce || rows.Count > 0)
      {
        PptxSlide newSlide = lastSlideTemplate.Clone();

        PptxTable table = newSlide.FindTables(tag).First();

        List<PptxTable.Cell[]> remainingRows = table.SetRows(rows);
        rows = remainingRows;

        PptxSlide.InsertAfter(newSlide, lastSlide);
        lastSlide = newSlide;
        slidesCreated.Add(newSlide);

        if (loopOnce) loopOnce = false;
      }

      lastSlideTemplate.Remove();

      return slidesCreated;
    }
  }
}