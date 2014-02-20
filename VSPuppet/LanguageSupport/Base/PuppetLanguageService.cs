/***************************************************************************

Copyright (c) Microsoft Corporation. All rights reserved.

***************************************************************************/

namespace Puppet
{
    using Microsoft.VisualStudio.Package;
    using Microsoft.VisualStudio.TextManager.Interop;
    using System;

    public abstract class PuppetLanguageService : Microsoft.VisualStudio.Package.LanguageService
    {
/*       
 *      #region Custom Colors

        public override int GetColorableItem(int index, out IVsColorableItem item)
        {
            if (index <= Configuration.ColorableItems.Count)
            {
                item = Configuration.ColorableItems[index - 1];
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }
            else
            {
                throw new ArgumentNullException("index");
            }
        }

        public override int GetItemCount(out int count)
        {
            count = Configuration.ColorableItems.Count;
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }
        #endregion
*/
        #region MPF Accessor and Factory specialisation

        private LanguagePreferences preferences;
        
        public override LanguagePreferences GetLanguagePreferences()
        {
            if (this.preferences == null)
            {
                this.preferences = 
                    new LanguagePreferences(this.Site,
                        typeof(Puppet.LanguageService).GUID,
                        this.Name);

                this.preferences.Init();
            }

            return this.preferences;
        }

        public override Source CreateSource(IVsTextLines buffer)
        {
            return new PuppetSource(this, buffer, this.GetColorizer(buffer));
        }

        private IScanner scanner;

        public override IScanner GetScanner(IVsTextLines buffer)
        {
            if (scanner == null)
                this.scanner = new LineScanner();

            return this.scanner;
        }
        #endregion

        public override void OnIdle(bool periodic)
        {
            // from IronPythonLanguage sample
            // this appears to be necessary to get a parse request with ParseReason = Check?
            Source src = (Source) GetSource(this.LastActiveTextView);
            if (src != null && src.LastParseTime >= Int32.MaxValue >> 12)
            {
                src.LastParseTime = 0;
            }

            base.OnIdle(periodic);
        }

        public override AuthoringScope ParseSource(ParseRequest req)
        {
            PuppetSource source = (PuppetSource)this.GetSource(req.FileName);
            bool yyparseResult = false;

            // req.DirtySpan seems to be set even though no changes have occurred
            // source.IsDirty also behaves strangely
            // might be possible to use source.ChangeCount to sync instead

            if (req .Sink.Reason == ParseReason.Check || req.DirtySpan.iStartIndex != req.DirtySpan.iEndIndex
                || req.DirtySpan.iStartLine != req.DirtySpan.iEndLine)
            {
                Puppet.Parser.ErrorHandler handler = new Parser.ErrorHandler();
                Puppet.Lexer.Scanner scanner = new Lexer.Scanner(); // string interface
                Parser.Parser parser = new PuppetParser();  // use noarg constructor

                parser.scanner = scanner;
                scanner.Handler = handler;
                parser.SetHandler(handler);
                scanner.SetSource(req.Text, 0);

                parser.MBWInit(req);
                yyparseResult = parser.Parse();

                // store the parse results
                // source.ParseResult = aast;
                source.ParseResult = null;
                source.Braces = parser.Braces;

                // for the time being, just pull errors back from the error handler
                if (handler.ErrNum > 0)
                {
                    foreach (Parser.Error error in handler.SortedErrorList())
                    {
                        TextSpan span = new TextSpan();
                        span.iStartLine = span.iEndLine = error.line - 1;
                        span.iStartIndex = error.column;
                        span.iEndIndex = error.column + error.length;
                        req.Sink.AddError(req.FileName, error.message, span, Severity.Error);
                    }
                }
            }

            switch (req.Reason)
            {
                case ParseReason.Check:
                case ParseReason.HighlightBraces:
                case ParseReason.MatchBraces:
                case ParseReason.MemberSelectAndHighlightBraces:
                    int indexOfCaret = source.GetPositionOfLineIndex(req.Line, req.Col);
                    // send matches to sink
                    // this should (probably?) be filtered on req.Line / col
                    if (source.Braces != null)
                    {
                        foreach (TextSpan[] brace in source.Braces)
                        {
                            if (brace.Length == 2)
                                req.Sink.MatchPair(brace[0], brace[1], 1);
                            else if (brace.Length >= 3)
                                req.Sink.MatchTriple(brace[0], brace[1], brace[2], 1);
                        }
                    }
                    break;
                default:
                    break;
            }

            return new PuppetAuthoringScope(source.ParseResult);
        }

        public override string Name
        {
            get { return Configuration.Name; }
        }
    }
}