using DocumentFormat.OpenXml.Bibliography;

namespace PptxTemplater
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using DocumentFormat.OpenXml.Packaging;
    using DocumentFormat.OpenXml.Presentation;

    using A = DocumentFormat.OpenXml.Drawing;
    using Picture = DocumentFormat.OpenXml.Presentation.Picture;

    /// <summary>
    /// Represents a slide inside a PowerPoint file.
    /// </summary>
    /// <remarks>Could not simply be named Slide, conflicts with DocumentFormat.OpenXml.Drawing.Slide.</remarks>
    public class PptxSlide
    {
        /// <summary>
        /// Holds the presentation part.
        /// </summary>
        private readonly PresentationPart _presentationPart;

        /// <summary>
        /// Holds the slide part.
        /// </summary>
        private readonly SlidePart _slidePart;

        /// <summary>
        /// Initializes a new instance of the <see cref="PptxSlide"/> class.
        /// </summary>
        /// <param name="presentationPart">The presentation part.</param>
        /// <param name="slidePart">The slide part.</param>
        internal PptxSlide(PresentationPart presentationPart, SlidePart slidePart)
        {
            this._presentationPart = presentationPart;
            this._slidePart = slidePart;
        }

        /// <summary>
        /// Gets all the texts found inside the slide.
        /// </summary>
        /// <returns>The list of texts detected into the slide.</returns>
        /// <remarks>
        /// Some strings inside the array can be empty, this happens when all A.Text from a paragraph are empty
        /// <see href="http://msdn.microsoft.com/en-us/library/office/cc850836">How to: Get All the Text in a Slide in a Presentation</see>
        /// </remarks>
        public IEnumerable<string> GetTexts()
        {
            return this._slidePart.Slide.Descendants<A.Paragraph>().Select(PptxParagraph.GetTexts);
        }

        /// <summary>
        /// Gets the slide title if any.
        /// </summary>
        /// <returns>The title or an empty string.</returns>
        public string GetTitle()
        {
            var title = string.Empty;

            // Find the title if any
            var titleShape = this._slidePart.Slide.Descendants<Shape>().FirstOrDefault(IsShapeATitle);
            if (titleShape != null)
            {
                title = string.Join(" ", titleShape.Descendants<A.Paragraph>().Select(PptxParagraph.GetTexts));
            }

            return title;
        }

        /// <summary>
        /// Gets all the notes associated with the slide.
        /// </summary>
        /// <returns>All the notes.</returns>
        /// <remarks>
        /// <see href="http://msdn.microsoft.com/en-us/library/office/gg278319.aspx">Working with Notes Slides</see>
        /// </remarks>
        public IEnumerable<string> GetNotes()
        {
            var notes = new List<string>();
            if (this._slidePart.NotesSlidePart != null)
            {
                notes.AddRange(this._slidePart.NotesSlidePart.NotesSlide.Descendants<A.Paragraph>().Select(PptxParagraph.GetTexts));
            }
            return notes;
        }

        /// <summary>
        /// Gets all the tables associated with the slide.
        /// </summary>
        /// <returns>All the tables.</returns>
        /// <remarks>Assigns an "artificial" id (tblId) to the tables that match the tag.</remarks>
        public IEnumerable<PptxTable> GetTables()
        {
            var tables = new List<PptxTable>();

            var tblId = 0;
            foreach (var graphicFrame in this._slidePart.Slide.Descendants<GraphicFrame>())
            {
              if (graphicFrame.NonVisualGraphicFrameProperties == null) continue;
              
              var cNvPr = graphicFrame.NonVisualGraphicFrameProperties.NonVisualDrawingProperties;

              if (cNvPr != null && cNvPr.Title == null) continue;
             
              var title = cNvPr.Title.Value;
              tables.Add(new PptxTable(this, tblId, title));
              tblId++;
            }

            return tables;
        }

        /// <summary>
        /// Finds a table given its tag inside the slide.
        /// </summary>
        /// <param name="tag">The tag associated with the table so it can be found.</param>
        /// <returns>The table or null.</returns>
        public IEnumerable<PptxTable> FindTables(string tag)
        {
            return this.GetTables().Where(table => table.Title.Contains(tag));
        }

        /// <summary>
        /// Type of replacement to perform inside ReplaceTag().
        /// </summary>
        public enum ReplacementType
        {
            /// <summary>
            /// Replaces the tags everywhere.
            /// </summary>
            Global,

            /// <summary>
            /// Does not replace tags that are inside a table.
            /// </summary>
            NoTable
        }

        /// <summary>
        /// Replaces a text (tag) by another inside the slide.
        /// </summary>
        /// <param name="tag">The tag to replace by newText, if null or empty do nothing; tag is a regex string.</param>
        /// <param name="newText">The new text to replace the tag with, if null replaced by empty string.</param>
        /// <param name="replacementType">The type of replacement to perform.</param>
        public void ReplaceTag(string tag, string newText, ReplacementType replacementType)
        {
            foreach (var p in this._slidePart.Slide.Descendants<A.Paragraph>())
            {
                switch (replacementType)
                {
                    case ReplacementType.Global:
                        PptxParagraph.ReplaceTag(p, tag, newText);
                        break;

                    case ReplacementType.NoTable:
                        var tables = p.Ancestors<A.Table>();
                        if (!tables.Any())
                        {
                            // If the paragraph has no table ancestor
                            PptxParagraph.ReplaceTag(p, tag, newText);
                        }
                        break;
                }
            }

            this.Save();
        }

        /// <summary>
        /// Replaces a text (tag) by another inside the slide given a PptxTable.Cell.
        /// This is a convenient method that overloads the original ReplaceTag() method.
        /// </summary>
        /// <param name="tagPair">The tag/new text, BackgroundPicture is ignored.</param>
        /// <param name="replacementType">The type of replacement to perform.</param>
        public void ReplaceTag(PptxTable.Cell tagPair, ReplacementType replacementType)
        {
            this.ReplaceTag(tagPair.Tag, tagPair.NewText, replacementType);
        }

        /// <summary>
        /// Replaces a picture by another inside the slide.
        /// </summary>
        /// <param name="tag">The tag associated with the original picture so it can be found, if null or empty do nothing.</param>
        /// <param name="newPicture">The new picture (as a byte array) to replace the original picture with, if null do nothing.</param>
        /// <param name="contentType">The picture content type: image/png, image/jpeg...</param>
        /// <remarks>
        /// <see href="http://stackoverflow.com/questions/7070074/how-can-i-retrieve-images-from-a-pptx-file-using-ms-open-xml-sdk">How can I retrieve images from a .pptx file using MS Open XML SDK?</see>
        /// <see href="http://stackoverflow.com/questions/7137144/how-can-i-retrieve-some-image-data-and-format-using-ms-open-xml-sdk">How can I retrieve some image data and format using MS Open XML SDK?</see>
        /// <see href="http://msdn.microsoft.com/en-us/library/office/bb497430.aspx">How to: Insert a Picture into a Word Processing Document</see>
        /// </remarks>
        public void ReplacePicture(string tag, byte[] newPicture, string contentType)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }

            if (newPicture == null)
            {
                return;
            }

            var imagePart = this.AddPicture(newPicture, contentType);

            foreach (var pic in this._slidePart.Slide.Descendants<Picture>())
            {
                var cNvPr = pic.NonVisualPictureProperties.NonVisualDrawingProperties;
                if (cNvPr.Title != null)
                {
                    var title = cNvPr.Title.Value;
                    if (title.Contains(tag))
                    {
                        // Gets the relationship ID of the part
                        var rId = this._slidePart.GetIdOfPart(imagePart);

                        pic.BlipFill.Blip.Embed.Value = rId;
                    }
                }
            }

            // Need to save the slide otherwise the relashionship is not saved.
            // Example: <a:blip r:embed="rId2">
            // r:embed is not updated with the right rId
            this.Save();
        }

        /// <summary>
        /// Replaces a picture by another inside the slide.
        /// </summary>
        /// <param name="tag">The tag associated with the original picture so it can be found, if null or empty do nothing.</param>
        /// <param name="newPictureFile">The new picture (as a file path) to replace the original picture with, if null do nothing.</param>
        /// <param name="contentType">The picture content type: image/png, image/jpeg...</param>
        public void ReplacePicture(string tag, string newPictureFile, string contentType)
        {
            var bytes = File.ReadAllBytes(newPictureFile);
            this.ReplacePicture(tag, bytes, contentType);
        }

        /// <summary>
        /// Clones this slide.
        /// </summary>
        /// <returns>The clone.</returns>
        /// <remarks>
        /// <see href="http://blogs.msdn.com/b/brian_jones/archive/2009/08/13/adding-repeating-data-to-powerpoint.aspx">Adding Repeating Data to PowerPoint</see>
        /// <see href="http://startbigthinksmall.wordpress.com/2011/05/17/cloning-a-slide-using-open-xml-sdk-2-0/">Cloning a Slide using Open Xml SDK 2.0</see>
        /// <see href="http://www.exsilio.com/blog/post/2011/03/21/Cloning-Slides-including-Images-and-Charts-in-PowerPoint-presentations-Using-Open-XML-SDK-20-Productivity-Tool.aspx">See Cloning Slides including Images and Charts in PowerPoint presentations and Using Open XML SDK 2.0 Productivity Tool</see>
        /// </remarks>
        public PptxSlide Clone()
        {
            var slideTemplate = this._slidePart;

            // Clone slide contents
            var slidePartClone = this._presentationPart.AddNewPart<SlidePart>();
            using (var templateStream = slideTemplate.GetStream(FileMode.Open))
            {
                slidePartClone.FeedData(templateStream);
            }

            // Copy layout part
            slidePartClone.AddPart(slideTemplate.SlideLayoutPart);

            // Copy the image parts
            foreach (var image in slideTemplate.ImageParts)
            {
                var imageClone = slidePartClone.AddImagePart(image.ContentType, slideTemplate.GetIdOfPart(image));
                using (var imageStream = image.GetStream())
                {
                    imageClone.FeedData(imageStream);
                }
            }

            return new PptxSlide(this._presentationPart, slidePartClone);
        }

        /// <summary>
        /// Inserts this slide after a given target slide.
        /// </summary>
        /// <param name="newSlide">The new slide to insert.</param>
        /// <param name="prevSlide">The previous slide.</param>
        /// <remarks>
        /// This slide will be inserted after the slide specified as a parameter.
        /// <see href="http://startbigthinksmall.wordpress.com/2011/05/17/cloning-a-slide-using-open-xml-sdk-2-0/">Cloning a Slide using Open Xml SDK 2.0</see>
        /// </remarks>
        public static void InsertAfter(PptxSlide newSlide, PptxSlide prevSlide)
        {
            // Find the presentationPart
            var presentationPart = prevSlide._presentationPart;

            var slideIdList = presentationPart.Presentation.SlideIdList;

            // Find the slide id where to insert our slide
            SlideId prevSlideId = null;
            foreach (SlideId slideId in slideIdList.ChildElements)
            {
                // See http://openxmldeveloper.org/discussions/development_tools/f/17/p/5302/158602.aspx
                if (slideId.RelationshipId == presentationPart.GetIdOfPart(prevSlide._slidePart))
                {
                    prevSlideId = slideId;
                    break;
                }
            }

            // Find the highest id
            var maxSlideId = slideIdList.ChildElements.Cast<SlideId>().Max(x => x.Id.Value);

            // public override T InsertAfter<T>(T newChild, DocumentFormat.OpenXml.OpenXmlElement refChild)
            // Inserts the specified element immediately after the specified reference element.
            var newSlideId = slideIdList.InsertAfter(new SlideId(), prevSlideId);
            newSlideId.Id = maxSlideId + 1;
            newSlideId.RelationshipId = presentationPart.GetIdOfPart(newSlide._slidePart);
        }

        /// <summary>
        /// Removes the slide from the PowerPoint file.
        /// </summary>
        /// <remarks>
        /// <see href="http://msdn.microsoft.com/en-us/library/office/cc850840.aspx">How to: Delete a Slide from a Presentation</see>
        /// </remarks>
        public void Remove()
        {
            var slideIdList = this._presentationPart.Presentation.SlideIdList;

            if (slideIdList != null)
              foreach (var slideId in slideIdList.ChildElements.Cast<SlideId>()
                         .Where(slideId => slideId.RelationshipId == this._presentationPart.GetIdOfPart(this._slidePart)))
              {
                slideIdList.RemoveChild(slideId);
                break;
              }

            this._presentationPart.DeletePart(this._slidePart);
        }

        /// <summary>
        /// Determines whether the given shape is a title.
        /// </summary>
        private static bool IsShapeATitle(Shape sp)
        {
            var isTitle = false;

            if (sp.NonVisualShapeProperties == null || sp.NonVisualShapeProperties.ApplicationNonVisualDrawingProperties == null) return isTitle;



            var ph = sp.NonVisualShapeProperties.ApplicationNonVisualDrawingProperties.GetFirstChild<PlaceholderShape>();

            if (ph?.Type == null || !ph.Type.HasValue) return isTitle;


            var placeholderType = (PlaceholderValues)ph.Type;
            if (placeholderType.Equals(PlaceholderValues.Title) || placeholderType.Equals(PlaceholderValues.CenteredTitle))
            {
              isTitle = true;
            }

            return isTitle;
        }

        /// <summary>
        /// Adds a new picture to the slide in order to re-use the picture later on.
        /// </summary>
        /// <param name="picture">The picture as a byte array.</param>
        /// <param name="contentType">The picture content type: image/png, image/jpeg...</param>
        /// <returns>The image part</returns>
        internal ImagePart AddPicture(byte[] picture, string contentType)
        {
          var type = new PartTypeInfo();
            switch (contentType)
            {
                case "image/bmp":
                    type = ImagePartType.Bmp;
                    break;
                case "image/emf": // TODO
                    type = ImagePartType.Emf;
                    break;
                case "image/gif": // TODO
                    type = ImagePartType.Gif;
                    break;
                case "image/ico": // TODO
                    type = ImagePartType.Icon;
                    break;
                case "image/jpeg":
                    type = ImagePartType.Jpeg;
                    break;
                case "image/pcx": // TODO
                    type = ImagePartType.Pcx;
                    break;
                case "image/png":
                    type = ImagePartType.Png;
                    break;
                case "image/tiff": // TODO
                    type = ImagePartType.Tiff;
                    break;
                case "image/wmf": // TODO
                    type = ImagePartType.Wmf;
                    break;
            }

            var imagePart = this._slidePart.AddImagePart(type);

            // FeedData() closes the stream and we cannot reuse it (ObjectDisposedException)
            // solution: copy the original stream to a MemoryStream
            using (var stream = new MemoryStream(picture))
            {
                imagePart.FeedData(stream);
            }

            // No need to detect duplicated images
            // PowerPoint do it for us on the next manual save

            return imagePart;
        }

        /// <summary>
        /// Gets the relationship ID of a given image part.
        /// </summary>
        /// <param name="imagePart">The image part.</param>
        /// <returns>The relationship ID of the image part.</returns>
        internal string GetIdOfImagePart(ImagePart imagePart)
        {
            return this._slidePart.GetIdOfPart(imagePart);
        }

        /// <summary>
        /// Finds a table (a:tbl) given its "artificial" id (tblId).
        /// </summary>
        /// <param name="tblId">The table id.</param>
        /// <returns>The table or null if not found.</returns>
        /// <remarks>The "artificial" id (tblId) is created inside FindTables().</remarks>
        internal A.Table FindTable(int tblId)
        {
            A.Table tbl = null;

            var graphicFrames = this._slidePart.Slide.Descendants<GraphicFrame>();
            var graphicFrame = graphicFrames.ElementAt(tblId);
            if (graphicFrame != null)
            {
                tbl = graphicFrame.Descendants<A.Table>().First();
            }

            return tbl;
        }

        /// <summary>
        /// Removes a table (a:tbl) given its "artificial" id (tblId).
        /// </summary>
        /// <param name="tblId">The table id.</param>
        /// <remarks>
        /// <![CDATA[
        /// p:graphicFrame
        ///  a:graphic
        ///   a:graphicData
        ///    a:tbl (Table)
        /// ]]>
        /// </remarks>
        internal void RemoveTable(int tblId)
        {
            var graphicFrames = this._slidePart.Slide.Descendants<GraphicFrame>();
            var graphicFrame = graphicFrames.ElementAt(tblId);
            graphicFrame.Remove();
        }

        /// <summary>
        /// Saves the slide.
        /// </summary>
        /// <remarks>
        /// This is mandatory to save the slides after modifying them otherwise
        /// the next manipulation that will be performed on the pptx won't
        /// include the modifications done before.
        /// </remarks>
        internal void Save()
        {
            this._slidePart.Slide.Save();
        }
    }
}
