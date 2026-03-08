// FF.Application/Common/Models/ImportResult.cs
namespace FF.Application.Common.Models
{
    public class ImportResult
    {
        public int Inserted { get; init; }
        public int Replaced { get; init; }
        public int Failed { get; init; }
        public List<string> Errors { get; init; } = [];

        public int Total => Inserted + Replaced;
        public bool HasErrors => Errors.Count != 0;
    }
}