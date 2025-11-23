using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyApp.Namespace.Services;

namespace MyApp.Namespace
{
    [Route("api/[controller]")]
    [ApiController]
    public class ToDoController : ControllerBase
    {
        private readonly DatabaseService _db;

        public ToDoController(DatabaseService db)
        {
            _db = db;
        }

        // GET: api/<ToDoController>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                // Example: Query all todos
                // var todos = await _db.QueryAsync("SELECT * FROM todos");
                // return Ok(todos);
                
                return Ok(new string[] { "value1", "value2" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET api/<ToDoController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<ToDoController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<ToDoController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<ToDoController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
