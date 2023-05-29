using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using PaymentAPI.Model;
using PaymentAPI.Services;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PaymentAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CreditCardController : ControllerBase
    {
        //readonly DiscoveryHttpClientHandler _discoveryHttpClientHandler;
        ICreditCardService _creditCardService;

        public CreditCardController(ICreditCardService creditCardService/*, IDiscoveryClient discoveryClient*/)
        {
            _creditCardService = creditCardService;
            //_discoveryHttpClientHandler = new(discoveryClient);
        }

        //[HttpGet("test")]
        //public async Task<IActionResult> GetTest()
        //{
        //    var data = await _creditCardService.GetAllAsync();
        //    return Ok(data.Data);
        //}

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _creditCardService.GetAllAsync();
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _creditCardService.GetByIdAsync(id);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            var result = await _creditCardService.GetByUserIdAsync(userId);
            if (result.Success)
            {
                return Ok(result.Data);
            }
            return BadRequest(result);
        }
        [HttpPost]
        public async Task<IActionResult> Add(CreditCard creditCard)
        {
            var result = await _creditCardService.AddAsync(creditCard);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(CreditCard creditCard)
        {
            var result = await _creditCardService.DeleteAsync(creditCard);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPut]
        public async Task<IActionResult> Update(CreditCard creditCard)
        {
            var result = await _creditCardService.UpdateAsync(creditCard);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
    }
}

