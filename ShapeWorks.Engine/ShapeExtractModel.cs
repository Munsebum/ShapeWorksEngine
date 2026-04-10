using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeWorks.Engine
{
    public class ShapeExtractRequest
    {
        public List<ESItem> ES { get; set; }
    }

    public class ESItem
    {
        public string LibraryElementName { get; set; }
        public string RevitElementUniqueId { get; set; }
        public string ReviewItem { get; set; }
        public List<SectionReviewItem> SectionReviewList { get; set; }
        public List<PSETItem> PSETList { get; set; }
    }

    public class SectionReviewItem
    {
        public string DrivingDirection { get; set; }
        public string SpanNo { get; set; }
        public string PartNo { get; set; }
        public string SectionDirection { get; set; }
        public string SectionPosition { get; set; }
        public string DetailPosition { get; set; }
        public string EntryExitPosition { get; set; }
        public string PierAbutmentPosition { get; set; }
    }

    public class PSETItem
    {
        public string ReviewElementName { get; set; }
        public string RuleCode { get; set; }
        public List<VariableItem> VariableList { get; set; }
    }

    public class VariableItem
    {
        public string VariableKind { get; set; }
        public string VariableHangul { get; set; }
        public string VariableName { get; set; }
        public string VariableValue { get; set; }
        public string VariableUnit { get; set; }
        public string ParentCode { get; set; }
    }
}
