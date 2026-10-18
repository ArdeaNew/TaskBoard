using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    internal static class BoardPrintDocument
    {
        internal static FixedDocument Create(BoardPage page)
        {
            if (page == null) throw new ArgumentNullException("page");
            var fixedPage = new FixedPage { Width = page.PageSize.Width, Height = page.PageSize.Height };
            fixedPage.Children.Add(new PageVisual(page));
            fixedPage.Measure(page.PageSize);
            fixedPage.Arrange(new Rect(page.PageSize));
            fixedPage.UpdateLayout();
            var content = new PageContent();
            ((IAddChild)content).AddChild(fixedPage);
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = page.PageSize;
            document.Pages.Add(content);
            return document;
        }
    }
}
