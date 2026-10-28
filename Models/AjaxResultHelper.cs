namespace MachineKarte.Web.Models
{
    public static class AjaxResult
    {
        public static AjaxResult<T> Ok<T>(T data)
        {
            return new AjaxResult<T>
            {
                Success = true,
                Data = data
            };
        }

        public static AjaxResult<object> Fail(string message)
        {
            return new AjaxResult<object>
            {
                Success = false,
                Message = message
            };
        }
    }
}