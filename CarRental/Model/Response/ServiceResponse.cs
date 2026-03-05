namespace CarRental.Model.Response
{
    public class ServiceResponse<T>
    {
        public int StatusCode { get; set; }
        public string? Message { get; set; }
        public T Data { get; set; }
    }
}
