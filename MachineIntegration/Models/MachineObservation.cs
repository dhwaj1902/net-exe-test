namespace MachineIntegration.Models;

public class MachineObservation
{
    public string LabNo { get; set; } = "";
    public string AssayNo { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string Age { get; set; } = "";
    public string Gender { get; set; } = "";
}

public class MachineReading
{
    public string LabNo { get; set; } = "";
    public string MachineId { get; set; } = "";
    public string MachineParam { get; set; } = "";
    public string Reading { get; set; } = "";
    public bool IsImage { get; set; } = false;
    public string ImageType { get; set; } = "";
    public string ImageUrl { get; set; } = "";
}

