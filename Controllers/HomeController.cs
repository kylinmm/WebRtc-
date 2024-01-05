using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net;
using WebRtc桌面共享.Models;

namespace WebRtc桌面共享.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IHttpContextAccessor _accessor;

        public HomeController(ILogger<HomeController> logger, IHttpContextAccessor accessor)
        {
            _logger = logger;
            _accessor = accessor;
        }

        public IActionResult Index()
        {
            var ipadress = _accessor.HttpContext.Connection.RemoteIpAddress;


            ViewData["USERID"] = System.Guid.NewGuid().ToString("N");
            ViewData["IP"] = ipadress.MapToIPv4().ToString();
            ViewData["Host"] = Request.Headers["Host"];
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }


    
    }
}