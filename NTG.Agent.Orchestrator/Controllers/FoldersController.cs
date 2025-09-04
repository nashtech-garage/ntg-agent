using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Knowledge;
using NTG.Agent.Orchestrator.Models.Documents;
using NTG.Agent.Shared.Dtos.Folders;

namespace NTG.Agent.Orchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class FoldersController : ControllerBase
{
    private readonly AgentDbContext _agentDbContext;
    private readonly IKnowledgeService _knowledgeService;

    public FoldersController(AgentDbContext agentDbContext, IKnowledgeService knowledgeService)
    {
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _knowledgeService = knowledgeService;
    }

    /// <summary>
    /// Retrieves a list of folders, optionally filtered by the specified agent ID.
    /// </summary>
    /// <remarks>If <paramref name="agentId"/> is not provided, all folders in the database are
    /// returned.</remarks>
    /// <param name="agentId">An optional parameter representing the unique identifier of an agent.  If provided, only folders associated with
    /// the specified agent are returned.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains an  <see cref="ActionResult{T}"/> of
    /// <see cref="IEnumerable{T}"/> containing the list of folders.  Each folder includes its associated children and
    /// documents.</returns>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Folder>>> GetFolders([FromQuery] Guid? agentId)
    {
        var query = _agentDbContext.Folders
            .Include(f => f.Children)
            .Include(f => f.Documents)
            .AsQueryable();

        if (agentId.HasValue)
        {
            query = query.Where(f => f.AgentId == agentId.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// Retrieves a folder by its unique identifier, including its child folders and associated documents.
    /// </summary>
    /// <remarks>The folder is retrieved from the database along with its related child folders and documents.
    /// Ensure the provided <paramref name="id"/> is a valid <see cref="Guid"/>.</remarks>
    /// <param name="id">The unique identifier of the folder to retrieve.</param>
    /// <returns>An <see cref="ActionResult{T}"/> containing the folder with its children and documents if found; otherwise, a
    /// <see cref="NotFoundResult"/> if the folder does not exist.</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<Folder>> GetFolder(Guid id)
    {
        var folder = await _agentDbContext.Folders
            .Include(f => f.Children)
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (folder == null)
        {
            return NotFound();
        }

        return folder;
    }

    /// <summary>
    /// Creates a new folder with the specified details.
    /// </summary>
    /// <remarks>The created folder is associated with the authenticated user, who is recorded as both the
    /// creator and updater of the folder.</remarks>
    /// <param name="folderToCreate">The data transfer object containing the details of the folder to create, including its name, parent folder ID,
    /// and agent ID.</param>
    /// <returns>An <see cref="ActionResult{T}"/> containing the created <see cref="Folder"/> object and a 201 Created response
    /// if successful. Returns a 400 Bad Request response if the provided data is invalid.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [HttpPost]
    public async Task<ActionResult<Folder>> CreateFolder(CreateFolderDto folderToCreate)
    {
        if(!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");

        var folder = new Folder
        {
            Name = folderToCreate.Name,
            ParentId = folderToCreate.ParentId,
            AgentId = folderToCreate.AgentId,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };

        _agentDbContext.Folders.Add(folder);
        await _agentDbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetFolder), new { id = folder.Id }, folder);
    }

    /// <summary>
    /// Updates the details of an existing folder.
    /// </summary>
    /// <remarks>This method requires the user to be authenticated. The folder's details, including its name,
    /// parent folder, and associated agent,  are updated based on the provided <paramref name="updatedFolder"/> object.
    /// The user performing the update is recorded as the updater.</remarks>
    /// <param name="id">The unique identifier of the folder to update.</param>
    /// <param name="updatedFolder">An object containing the updated folder details.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation: <list type="bullet">
    /// <item><description><see cref="BadRequestResult"/> if the <paramref name="id"/> does not match the folder ID in
    /// <paramref name="updatedFolder"/>.</description></item> <item><description><see
    /// cref="UnauthorizedAccessException"/> if the user is not authenticated.</description></item>
    /// <item><description><see cref="NotFoundResult"/> if the folder with the specified <paramref name="id"/> does not
    /// exist.</description></item> <item><description><see cref="NoContentResult"/> if the folder is successfully
    /// updated.</description></item> </list></returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateFolder(Guid id, UpdateFolderDto updatedFolder)
    {
        if (id != updatedFolder.Id)
        {
            return BadRequest();
        }
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");

        var folder = await _agentDbContext.Folders.FindAsync(id);
        if (folder == null)
        {
            return NotFound();
        }

        folder.Name = updatedFolder.Name;
        folder.ParentId = updatedFolder.ParentId;
        folder.AgentId = updatedFolder.AgentId;
        folder.UpdatedByUserId = userId;

        _agentDbContext.Entry(folder).State = EntityState.Modified;
        await _agentDbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Deletes the specified folder and its associated documents, if applicable.
    /// </summary>
    /// <remarks>This method performs the following checks before deleting the folder: <list type="bullet">
    /// <item><description>Ensures the folder exists in the database.</description></item> <item><description>Validates
    /// that the folder is deletable and does not contain child folders.</description></item> </list> If the folder
    /// contains associated documents, they are also removed from the database and, if applicable, from the knowledge
    /// base.</remarks>
    /// <param name="id">The unique identifier of the folder to delete.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation: <list type="bullet">
    /// <item><description><see cref="NotFoundResult"/> if the folder with the specified <paramref name="id"/> does not
    /// exist.</description></item> <item><description><see cref="BadRequestResult"/> if the folder cannot be deleted
    /// because it is a system folder or contains child folders.</description></item> <item><description><see
    /// cref="NoContentResult"/> if the folder and its associated documents are successfully
    /// deleted.</description></item> </list></returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFolder(Guid id)
    {
        var folder = await _agentDbContext.Folders
            .Include(f => f.Children)
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (folder == null)
        {
            return NotFound();
        }

        if (!folder.IsDeletable)
        {
            return BadRequest("This folder cannot be deleted as it is a system folder.");
        }

        if (folder.Children.Any())
        {
            return BadRequest("Cannot delete folder with child folders.");
        }

        _agentDbContext.Folders.Remove(folder);
        // Remove associated documents from the folder
        if (folder.Documents.Any())
        {
            _agentDbContext.Documents.RemoveRange(folder.Documents);
            // Remove associated documents from knowledge base if they exist
            foreach (var document in folder.Documents)
            {
                if (document.KnowledgeDocId != null)
                {
                    await _knowledgeService.RemoveDocumentAsync(document.KnowledgeDocId, document.AgentId);
                }
            }
        }

        await _agentDbContext.SaveChangesAsync();
        return NoContent();
    }
}