namespace api.Models
{
    public class Threat
    {
        public int? id { get; set; }
        public string title { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public string category { get; set; } = string.Empty;
        public string source { get; set; } = string.Empty;
        public DateTime date_observed { get; set; }
        public string impact_level { get; set; } = string.Empty;
        public string? external_reference { get; set; }
        public string status { get; set; } = "New";
        public int? user_id { get; set; }
        public DateTime? created_at { get; set; }
        public DateTime? updated_at { get; set; }
    }
}
