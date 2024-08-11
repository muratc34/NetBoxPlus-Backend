using Auth.Application.Users.GetUserById;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public sealed class UsersController : ControllerBase
    {
        private readonly ISender _sender;

        public UsersController(ISender sender)
        {
            _sender = sender;
        }

        [HttpGet]
        [Route("{userId}")]
        [Authorize]
        public async Task<IActionResult> GetUserById(Guid userId)
        {
            var result = await _sender.Send(new GetUserByIdQuery(userId));
            return result.IsSuccess ? Ok(result) : NotFound(result.Error);
        }
    }
}
