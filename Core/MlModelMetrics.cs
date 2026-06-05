using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace КР_Ханников.Core
{
    public class MlModelMetrics
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ModelType { get; set; } = string.Empty;

        public DateTime TrainedAt { get; set; } = DateTime.UtcNow;

        public double MicroAccuracy { get; set; }
        public double MacroAccuracy { get; set; }
        public double LogLoss { get; set; }
        public int SampleCount { get; set; }
    }
}
