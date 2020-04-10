namespace Tiny7z.Archive
{
	public abstract class Archive
    {
        public virtual bool IsValid
        {
            get; protected set;
        }
        public abstract IExtractor Extractor();
        public abstract ICompressor Compressor();
    }
}
