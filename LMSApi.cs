using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Runtime.Serialization;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.IO;
using System.Text.RegularExpressions;
using DTO = Convergence.Training.Server.DTO;
using APIDTO = Convergence.Training.Server.Web.Services4.API.DTO;
using log4net;
using System.Net;
using System.Reflection;

namespace Convergence.LMSApiLib
{
	public class LMSApi
	{
		private string _username, _secret, _secretKey, _baseUrl;
		private OAuthBase _oauth;
		private List<NodeFacade> _nodeCache = new List<NodeFacade>();
		private List<QualFacade> _qualCache = new List<QualFacade>();
		private List<ReqFacade> _reqCache = new List<ReqFacade>();
		private List<AttributeValueFacade> _attCache = new List<AttributeValueFacade>();
		private List<ThumbnailFacade> _thumbCache = new List<ThumbnailFacade>();

		private static readonly ILog log = LogManager.GetLogger(typeof(LMSApi));

		public LMSApi(string username, string secret, string secretKey, string baseUrl)
		{
			this._username = username;
			this._secret = secret;
			this._secretKey = secretKey;
			this._baseUrl = baseUrl;
			this._oauth = new OAuthBase();

			log.InfoFormat("LMSAPI Created, User: {0}, Secret: {1}, Url: {2}", _username, _secret, _baseUrl);

			SetAllowUnsafeHeaderParsing20();

			PingContentUploader();
		}

		public LMSApi(string username, string secret, string secretKey, string baseUrl, bool pingContentUploader)
		{
			this._username = username;
			this._secret = secret;
			this._secretKey = secretKey;
			this._baseUrl = baseUrl;
			this._oauth = new OAuthBase();

			log.InfoFormat("LMSAPI Created, User: {0}, Secret: {1}, Url: {2}", _username, _secret, _baseUrl);

			SetAllowUnsafeHeaderParsing20();

			if(pingContentUploader)
				PingContentUploader();
		}

		#region Users

		public UserFacade GetUserByUsername(int? contextNodeId, string username)
		{
			try
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					//qs.Name = username;
					qs.SearchString = username;
					qs.NodeState = DTO.Directory.NodeStates.Active;
					if (contextNodeId.HasValue)
						qs.ContextNodeID = contextNodeId.Value;

					string uri = _baseUrl + "/users";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));
					DTO.Base.User[] results = Deserialize<DTO.Base.User[]>(resultString);

					if (results.Length == 0)
						return null;
					else
					{
						DTO.Base.User result = results.Where(o => o.Username.ToLower() == username.ToLower()).FirstOrDefault();

						if (result != null)
						{
							UserFacade n = new UserFacade();
							n.NodeID = result.NodeID;
							n.Username = result.Username;
							n.UserID = result.UserID;
							n.Firstname = result.Firstname;
							n.Lastname = result.Lastname;
							n.Email = result.EMail;
							n.UserUID = result.UserUID;
							n.NodeUID = result.NodeUID.Value;

							return n;
						}
						else
							return null;
					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public UserFacade CreateUser(int? contextNodeId, string siteName, string departmentName, string teamName, string username, string firstname, string lastname, string email, string password, string externalId)
		{
			try
			{

				using (System.Net.WebClient wc = new System.Net.WebClient())
				{

					string uri = _baseUrl + "/create-user?username=" + username + "&firstname=" + firstname + "&lastname=" + lastname + "&email=" + email + "&password=" + password + "&site=" + siteName + "&department=" + departmentName + "&team=" + teamName + "&secretkey=" + _secretKey + "&externalid=" + externalId + "&phone=n/a&hiredate=" + String.Format("{0}-{1}-{2}", DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Year);
					string result = wc.DownloadString(uri);

					if (result.Contains("Created User"))
					{
						UserFacade newUser = GetUserByUsername(contextNodeId, username);
						return newUser;
					}
					else
					{
						throw new Exception("Unable to create user: " + result);
					}
				}

			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		#endregion

		#region Groups

		public List<APIDTO.GroupMemberItem> GetGroupMembers(Guid groupUid)
		{
			try
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{

					string uri = String.Format("{0}/group/{1}/members", _baseUrl, groupUid);
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);

					if (String.IsNullOrEmpty(resultString))
					{
						throw new Exception("Error getting group member(s) for '" + groupUid.ToString() + "'");
					}
					else
					{
						APIDTO.GroupMemberItem[] result = Deserialize<APIDTO.GroupMemberItem[]>(resultString);
						return result.ToList();
					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public bool AddGroupMembers(Guid groupUid, List<int> userNodeIds)
		{
			try
			{

				List<APIDTO.GroupMemberItem> newGroupMembers = new List<APIDTO.GroupMemberItem>();

				foreach (int userNodeId in userNodeIds)
				{
					APIDTO.GroupMemberItem newGroupMember = new APIDTO.GroupMemberItem(new DTO.Directory.GroupMember()
					{
						NodeState = DTO.Directory.NodeStates.Active,
						DTOState = DTO.State.Created
					});
					newGroupMember.GroupNodeUID = groupUid;
					newGroupMember.MemberNodeID = userNodeId;

					newGroupMembers.Add(newGroupMember);
				}

				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = String.Format("{0}/group/{1}/members", _baseUrl, groupUid);
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.GroupMemberItem[]>(newGroupMembers.ToArray()));

					if (String.IsNullOrEmpty(resultString))
					{
						throw new Exception("Error adding group member(s) to '" + groupUid.ToString() + "'");
					}
					else
					{
						DTO.ServiceResult<APIDTO.GroupMemberItem>[] result = Deserialize<DTO.ServiceResult<APIDTO.GroupMemberItem>[]>(resultString);

						int failCount = result.Count(r => r.IsSuccess() != true);
						int noNewMembersCount = result.Count(r => r.Message == "No new group members");

						if (failCount == 0 || noNewMembersCount == result.Length)
							return true;
						else
							return false;
					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return false;
		}

		public bool DeleteGroupMembers(Guid groupUid, List<int> userNodeIds)
		{
			try
			{

				List<APIDTO.GroupMemberItem> newGroupMembers = new List<APIDTO.GroupMemberItem>();

				foreach (int userNodeId in userNodeIds)
				{
					APIDTO.GroupMemberItem newGroupMember = new APIDTO.GroupMemberItem(new DTO.Directory.GroupMember()
					{
						NodeState = DTO.Directory.NodeStates.Active,
						DTOState = DTO.State.Deleted
					});
					newGroupMember.GroupNodeUID = groupUid;
					newGroupMember.MemberNodeID = userNodeId;

					newGroupMembers.Add(newGroupMember);
				}

				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = String.Format("{0}/group/{1}/members", _baseUrl, groupUid);
					string httpMethod = "PUT";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.GroupMemberItem[]>(newGroupMembers.ToArray()));

					if (String.IsNullOrEmpty(resultString))
					{
						throw new Exception("Error deleting group member(s) to '" + groupUid.ToString() + "'");
					}
					else
					{
						DTO.ServiceResult<APIDTO.GroupMemberItem>[] result = Deserialize<DTO.ServiceResult<APIDTO.GroupMemberItem>[]>(resultString);

						int failCount = result.Count(r => r.IsSuccess() != true);

						if (failCount == 0)
							return true;
						else
							return false;
					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return false;
		}

		public GroupFacade GetGroupByExternalID(int? contextNodeId, string searchString, string externalId)
		{
			try
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.SearchString = searchString;
					qs.NodeState = DTO.Directory.NodeStates.Active;
					qs.NodeType = DTO.Directory.NodeTypes.OrganizationalUnit;
					qs.NodeSubType = DTO.Directory.NodeSubTypes.Group;
					if (contextNodeId.HasValue)
						qs.ContextNodeID = contextNodeId.Value;

					string uri = _baseUrl + "/groups";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));
					DTO.Directory.Node[] groupNodes = Deserialize<DTO.Directory.Node[]>(resultString);

					if (groupNodes.Length == 0)
						return null;
					else
					{
						DTO.Directory.Node groupNode = groupNodes.FirstOrDefault();
						if (groupNode != null)
						{
							var result = GetGroupNodeByUID(groupNode.NodeUID.Value);
							if (result != null)
							{
								return result;
							}
						}
					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public GroupFacade GetGroupNodeByUID(Guid nodeUid)
		{
			try
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + "/node/" + nodeUid.ToString();
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);

					if (String.IsNullOrEmpty(resultString))
						return null;
					else
					{
						DTO.Directory.Node node = Deserialize<DTO.Directory.Node>(resultString);

						GroupFacade n = new GroupFacade();
						n.ParentID = node.ParentID.Value;
						n.Name = node.Name;
						n.NodeID = node.NodeID.Value;
						n.NodeUID = node.NodeUID.Value;
						n.TypeID = node.TypeID;
						if (node.SubTypeID.HasValue)
							n.SubTypeID = node.SubTypeID.Value;
						if (node.ObjectID.HasValue)
							n.ObjectID = node.ObjectID.Value;
						if (node.NodeDetail != null)
						{
							if (!String.IsNullOrEmpty(node.NodeDetail.ExternalID))
								n.ExternalID = node.NodeDetail.ExternalID;

							if (node.NodeDetail.DynamicGroupTargetNodeID != null)
								n.DynamicGroupTargetNodeID = node.NodeDetail.DynamicGroupTargetNodeID.Value;

							if (!String.IsNullOrEmpty(node.NodeDetail.DynamicGroupConditions))
								n.DynamicGroupLogic = node.NodeDetail.DynamicGroupConditions;

						}

						return n;
					}

				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public GroupFacade CreateGroup(int parentId, string name, string desc, int? groupTargetNodeId, string dynamicGroupLogic, string externalId)
		{
			try
			{
				DTO.Directory.Node newNode = new DTO.Directory.Node()
				{
					DTOState = DTO.State.Created,
					NodeState = DTO.Directory.NodeStates.Active,
					NodeDetail = new DTO.Directory.NodeDetail()
					{
						DTOState = DTO.State.Created,
						NodeState = DTO.Directory.NodeStates.Active,
					}
				};
				if (!String.IsNullOrEmpty(desc))
					newNode.NodeDetail.Description = desc;
				if (!String.IsNullOrEmpty(externalId))
					newNode.NodeDetail.ExternalID = externalId;
				newNode.ParentID = parentId;
				newNode.Name = name;
				if (groupTargetNodeId.HasValue && !String.IsNullOrEmpty(dynamicGroupLogic))
				{
					newNode.NodeDetail.DynamicGroup = true;
					newNode.NodeDetail.DynamicGroupConditions = dynamicGroupLogic;
					newNode.NodeDetail.DynamicGroupTargetNodeID = groupTargetNodeId;
				}
				else
				{
					newNode.NodeDetail.DynamicGroup = false;
				}
				newNode.TypeID = (int)DTO.Directory.NodeTypes.OrganizationalUnit;
				newNode.SubTypeID = (int)DTO.Directory.NodeSubTypes.Group;

				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + "/node";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Directory.Node>(newNode));

					if (String.IsNullOrEmpty(resultString))
					{
						throw new Exception("Error creating group '" + name + "'");
					}
					else
					{
						//returning this way because the create group result doesn't have the NodeUID
						return GetGroupByExternalID(parentId, name, externalId);
					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}



		#endregion

		#region Nodes

		public List<NodeFacade> GetNodes(int? contextNodeId, bool dontReturnUsers = false)
		{
			try
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.ContextNodeID = contextNodeId;
					qs.NodeState = DTO.Directory.NodeStates.Active;

					string uri = _baseUrl + "/nodes";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));
					DTO.Directory.Node[] results = Deserialize<DTO.Directory.Node[]>(resultString);

					if (results.Length == 0)
						return null;
					else
					{
						List<NodeFacade> nodes = new List<NodeFacade>();

						foreach (DTO.Directory.Node node in results)
						{
							if (dontReturnUsers == true && node.TypeID == (int)DTO.Directory.NodeTypes.Principal)
							{
								continue;
							}
							NodeFacade n = new NodeFacade();
							n.ParentID = node.ParentID.Value;
							n.Name = node.Name;
							n.NodeID = node.NodeID.Value;
							n.TypeID = node.TypeID;
							if (node.SubTypeID.HasValue)
								n.SubTypeID = node.SubTypeID.Value;
							if (node.ObjectID.HasValue)
								n.ObjectID = node.ObjectID.Value;

							nodes.Add(n);
						}

						_nodeCache.AddRange(nodes);

						return nodes;
					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public NodeFacade GetNode(int? contextNodeId, string name, DTO.Directory.NodeTypes nodeType, DTO.Directory.NodeSubTypes? nodeSubType)
		{
			string altName1 = String.Format("{0} {1}", name, nodeSubType.ToString());
			string altName2 = String.Format("{0} {1}", name, nodeType.ToString());
			var cachedObj = _nodeCache.Where(n => (n.Name == name || n.Name == altName1 || n.Name == altName2)
																				&& n.TypeID == (int)nodeType
																				&& (contextNodeId == null || (contextNodeId != null && n.ParentID == contextNodeId))
																				&& (nodeSubType == null || (nodeSubType != null && n.SubTypeID == (int)nodeSubType))
																				).FirstOrDefault();
			if (cachedObj != null)
				return cachedObj;
			else
			{
				try
				{
					using (System.Net.WebClient wc = new System.Net.WebClient())
					{
						APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
						qs.Name = name;
						qs.NodeType = nodeType;
						qs.NodeSubType = nodeSubType;
						qs.NodeState = DTO.Directory.NodeStates.Active;

						if (contextNodeId.HasValue)
							qs.ContextNodeID = contextNodeId.Value;
						else
							qs.ContextNodeID = 0;

						string uri = _baseUrl + "/nodes";
						string httpMethod = "POST";
						DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

						string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));
						DTO.Directory.Node[] results = Deserialize<DTO.Directory.Node[]>(resultString);

						if (results.Length == 0)
							return null;
						else
						{
							DTO.Directory.Node node = results.Where(r => r.Name == name).First();

							NodeFacade n = new NodeFacade();
							n.ParentID = node.ParentID.Value;
							n.Name = node.Name;
							n.NodeID = node.NodeID.Value;
							n.TypeID = node.TypeID;
							n.NodeUID = node.NodeUID.Value;
							if (node.SubTypeID.HasValue)
								n.SubTypeID = node.SubTypeID.Value;
							if (node.ObjectID.HasValue)
								n.ObjectID = node.ObjectID.Value;

							_nodeCache.Add(n);

							return n;

						}
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return null;
			}
		}

		public NodeFacade GetNodeById(int nodeId)
		{
			var cachedObj = _nodeCache.Where(n => n.NodeID == nodeId).FirstOrDefault();
			if (cachedObj != null)
				return cachedObj;
			else
			{
				try
				{
					using (System.Net.WebClient wc = new System.Net.WebClient())
					{
						APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
						qs.NodeState = DTO.Directory.NodeStates.Active;
						qs.NodeID = nodeId;

						string uri = _baseUrl + "/nodes";
						string httpMethod = "POST";
						DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

						string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));
						DTO.Directory.Node[] results = Deserialize<DTO.Directory.Node[]>(resultString);

						if (results.Length == 0)
							return null;
						else
						{
							DTO.Directory.Node node = results.First();

							NodeFacade n = new NodeFacade();
							n.ParentID = node.ParentID.Value;
							n.Name = node.Name;
							n.NodeID = node.NodeID.Value;
							n.NodeUID = node.NodeUID.Value;
							n.TypeID = node.TypeID;
							if (node.SubTypeID.HasValue)
								n.SubTypeID = node.SubTypeID.Value;
							if (node.ObjectID.HasValue)
								n.ObjectID = node.ObjectID.Value;

							_nodeCache.Add(n);

							return n;

						}
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return null;
			}
		}

		public NodeFacade GetOrganization(string name)
		{
			return GetNode(null, name, DTO.Directory.NodeTypes.Organization, null);
		}

		public NodeFacade GetRegion(string name)
		{
			return GetNode(null, name, DTO.Directory.NodeTypes.OrganizationalUnit, DTO.Directory.NodeSubTypes.Region);
		}

		public NodeFacade GetRegion(int parentId, string name)
		{
			return GetNode(parentId, name, DTO.Directory.NodeTypes.OrganizationalUnit, DTO.Directory.NodeSubTypes.Region);
		}

		public NodeFacade GetSite(int parentId, string name)
		{
			return GetNode(parentId, name, DTO.Directory.NodeTypes.OrganizationalUnit, DTO.Directory.NodeSubTypes.Site);
		}

		public NodeFacade GetDepartment(int parentId, string name)
		{
			return GetNode(parentId, name, DTO.Directory.NodeTypes.OrganizationalUnit, DTO.Directory.NodeSubTypes.Department);
		}

		public NodeFacade GetTeam(int parentId, string name)
		{
			return GetNode(parentId, name, DTO.Directory.NodeTypes.OrganizationalUnit, DTO.Directory.NodeSubTypes.Team);
		}

		public NodeFacade GetRepository(int parentId, string name)
		{
			return GetNode(parentId, name, DTO.Directory.NodeTypes.Repository, null);
		}

		public NodeFacade GetRegistry(int parentId, string name)
		{
			return GetNode(parentId, name, DTO.Directory.NodeTypes.Storage, DTO.Directory.NodeSubTypes.Registry);
		}

		public NodeFacade CreateNode(int parentId, string name, DTO.Directory.NodeSubTypes parentNodeSubType, DTO.Directory.NodeSubTypes nodeSubType)
		{
			try
			{
				DTO.Directory.Node newNode = CreateEmptyNode();
				newNode.Name = name;
				newNode.TypeID = (int)DTO.Directory.NodeTypes.OrganizationalUnit;
				newNode.SubTypeID = (int)nodeSubType;
				newNode.ParentID = parentId;
				newNode.ParentTypeID = (int)DTO.Directory.NodeTypes.OrganizationalUnit;
				newNode.ParentSubTypeID = ((int)parentNodeSubType);

				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + "/node";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Directory.Node>(newNode));

					if (String.IsNullOrEmpty(resultString))
					{
						throw new Exception("Error creation node '" + name + "'");
					}
					else
					{
						DTO.Directory.Node result = Deserialize<DTO.Directory.Node>(resultString);

						NodeFacade n = new NodeFacade();

						n.NodeID = result.NodeID.Value;
						n.Name = result.Name;
						n.TypeID = result.TypeID;
						if (result.SubTypeID.HasValue)
							n.SubTypeID = result.SubTypeID.Value;

						return n;
					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public NodeFacade CreateSite(int parentId, string name)
		{
			return CreateNode(parentId, name, DTO.Directory.NodeSubTypes.Region, DTO.Directory.NodeSubTypes.Site);
		}

		public NodeFacade CreateDepartment(int parentId, string name)
		{
			return CreateNode(parentId, name, DTO.Directory.NodeSubTypes.Site, DTO.Directory.NodeSubTypes.Department);
		}

		#endregion

		#region Qualifications

		public List<QualFacade> GetQualifications(int contextNodeId)
		{
			List<QualFacade> qualifications = new List<QualFacade>();

			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.ContextNodeID = contextNodeId;

					string uri = _baseUrl + "/qualifications";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

					DTO.Registry.Qualification[] results = Deserialize<DTO.Registry.Qualification[]>(resultString);

					if (results.Length > 0)
					{
						foreach (DTO.Registry.Qualification qualification in results)
						{
							QualFacade r = new QualFacade(qualification, contextNodeId);

							_qualCache.Add(r);
							qualifications.Add(r);
						}
					}
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return qualifications;
		}

		public List<QualFacade> GetQualifications(int contextNodeId, string search) //Used by CRMWebApps
		{
			List<QualFacade> qualifications = new List<QualFacade>();

			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.ContextNodeID = contextNodeId;
					qs.NodeState = DTO.Directory.NodeStates.Active;


					string uri = _baseUrl + "/qualification/includechildren?search=" + search.ToString();
					string httpMethod = "GET";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.DownloadString(uri);
					//Console.WriteLine(resultString);

					DTO.Registry.Qualification[] results = Deserialize<DTO.Registry.Qualification[]>(resultString);

					if (results.Length > 0)
					{
						foreach (DTO.Registry.Qualification qualification in results)
						{
							QualFacade r = new QualFacade(qualification, contextNodeId);

							if (r.ParentID == contextNodeId && qualification.StateID == 0)
								qualifications.Add(r);
						}
					}
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
					Console.WriteLine("Error in " + System.Reflection.MethodBase.GetCurrentMethod().Name + ": " + ex.ToString());
				}
			}
			return qualifications;
		}


		public QualFacade GetQualification(int parentId, string name, bool includeChildren)
		{
			var cachedObj = _qualCache.Where(n => n.ParentID == parentId && n.Name == name).FirstOrDefault();
			if (cachedObj != null)
				return cachedObj;
			else
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					try
					{
						NodeFacade registry = GetRegistry(parentId, name);
						if (registry == null)
							throw new Exception("Unable to find registry under " + parentId.ToString() + " named " + name);

						APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
						qs.Name = name;
						qs.NodeID = registry.NodeID;

						string uri = _baseUrl + "/qualifications";
						string httpMethod = "POST";
						DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

						string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

						DTO.Registry.Qualification[] results = Deserialize<DTO.Registry.Qualification[]>(resultString);

						if (results.Length == 0)
							return null;
						else
						{
							DTO.Registry.Qualification qualification = results.Where(r => r.Name == name).SingleOrDefault();

							if (qualification != null)
							{
								QualFacade r = new QualFacade(qualification, parentId);

								_qualCache.Add(r);

								return r;
							}
							else
							{
								return null;
							}

						}
					}
					catch (Exception ex)
					{
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
					}
					return null;
				}
			}
		}


		public QualFacade GetQualificationOld(int parentId, string name, bool includeChildren)
		{
			var cachedObj = _qualCache.Where(n => n.ParentID == parentId && n.Name == name).FirstOrDefault();
			if (cachedObj != null)
				return cachedObj;
			else
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					try
					{
						string uri = (includeChildren == true) ? (_baseUrl + "/qualification/includechildren?search=" + name) : (_baseUrl + "/qualification?search=" + name);
						string httpMethod = "GET";
						string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
						wc.Headers.Add("Authorization", authHeader);

						string resultString = wc.DownloadString(uri);

						if (String.IsNullOrEmpty(resultString))
							return null;
						else
						{
							DTO.Registry.Qualification[] results = Deserialize<DTO.Registry.Qualification[]>(resultString);

							if (results.Length == 1)
							{
								DTO.Registry.Qualification qualification = results.First();

								QualFacade q = new QualFacade(qualification, parentId);

								_qualCache.Add(q);

								return q;
							}
							else
							{
								return null;
							}
						}
					}
					catch (Exception ex)
					{
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
					}
					return null;
				}
			}
		}

		public DTO.Registry.Qualification GetQualificationById(int qualificationId)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/qualification/" + qualificationId.ToString();
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);

					if (String.IsNullOrEmpty(resultString))
						return null;
					else
					{
						DTO.Registry.Qualification qualification = Deserialize<DTO.Registry.Qualification>(resultString);

						return qualification;
					}
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return null;
			}
		}

		public QualFacade CreateQualification(int parentId, string parentName, string qualName, string sku, string externalID)
		{
			try
			{
				NodeFacade registryNode = GetRegistry(parentId, parentName);
				//GetNode(parentId, name, DTO.Directory.NodeTypes.Storage, DTO.Directory.NodeSubTypes.Registry);

				if (registryNode == null)
					throw new Exception("Error creating qualification - Unable to find registry with parentId '" + parentId + "' and name '" + qualName + "'");

				DTO.Registry.Qualification newObj = new DTO.Registry.Qualification();
				newObj.RegistryID = registryNode.ObjectID;
				newObj.Name = qualName;
				newObj.CompleteItemsModeID = DTO.Registry.CompleteItemsModes.AllItems;
				newObj.CompleteItemsOrderModeID = DTO.Registry.CompleteItemsOrderModes.AnyOrder;
				newObj.DTOState = DTO.State.Created;
				newObj.CreatedDate = DateTime.Now;
				newObj.QualificationRequirements = new List<DTO.Registry.QualificationRequirement>();
				newObj.SKU = sku;
				newObj.ExternalID = externalID;


				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + "/qualification";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Registry.Qualification>(newObj));
					DTO.ServiceResult<DTO.Registry.Qualification> result = Deserialize<DTO.ServiceResult<DTO.Registry.Qualification>>(resultString);

					if (result.IsSuccess() == true)
					{
						QualFacade q = new QualFacade();
						q.RegistryID = registryNode.ObjectID;
						q.QualificationID = result.ObjectIdentity.ObjectID.Value;
						q.Name = result.ObjectIdentity.Name;
						q.SKU = sku; //result.Object.SKU;
						q.ExternalID = externalID; // result.Object.ExternalID;

						q.RequirementIds = new List<int>();
						return q;
					}
					else
					{
						throw new Exception("Error creating Qualification '" + qualName + "': " + result.Message);
					}
				}
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

        // BJM on 5/12/2020 - Original UpdateQualification just had parameters for qualificationId and qualificationRequirements.  Since we need to be able to update name,
        // added the below overload.
        public QualFacade UpdateQualification(int qualificationId, List<ReqFacade> qualificationRequirements)
        {
            return UpdateQualification(qualificationId, qualificationRequirements, null);
        }

        public QualFacade UpdateQualification(int qualificationId, List<ReqFacade> qualificationRequirements, string name) //, QualFacade details
        {
            DTO.Registry.Qualification qual = GetQualificationById(qualificationId);

			if (qual == null)
				throw new Exception("Error updating qualification, unable to find existing qualification: " + qualificationId.ToString());

            if (!String.IsNullOrEmpty(name)) qual.Name = name;

            qual.DTOState = DTO.State.Updated;

			/*if(details != null)
			{
				if (!String.IsNullOrWhiteSpace(details.SKU))
					qual.SKU = details.SKU;
				if (!String.IsNullOrWhiteSpace(details.ExternalID))
					qual.ExternalID = details.ExternalID;
			}*/

			List<int> newRequirementIds = qualificationRequirements.Select(o => o.RequirementID).ToList();
			List<int> oldRequirementIds = qual.QualificationRequirements.OrderBy(o => o.DisplayOrder).Select(o => o.RequirementID).ToList();

            // BJM on 5/12/2020 - Since I added logic to update name via this method, I removed the IF below.
            //if (newRequirementIds.SequenceEqual(oldRequirementIds))
            //{
            //    return new QualFacade(qual, null);
            //}


			//find any current requirements not in the new requirement list and remove them
			for (int i = qual.QualificationRequirements.Count - 1; i >= 0; i--)
			{
				DTO.Registry.QualificationRequirement qr = qual.QualificationRequirements[i];
				/*if (qr.RequirementID == 244) //TODO: ehs spanish courses 
          continue;*/

				if (!newRequirementIds.Contains(qr.RequirementID))
					qual.QualificationRequirements.Remove(qr);

			}


			//go through each requirement and see if it needs to be added or updated
			int displayOrder = 1;
			for (int i = 0; i < qualificationRequirements.Count; i++)
			{
				ReqFacade newReq = qualificationRequirements[i];

				DTO.Registry.QualificationRequirement existingRequirement = qual.QualificationRequirements.Where(o => o.RequirementID == newReq.RequirementID).SingleOrDefault();
				if (existingRequirement == null)
				{
					//not already in qualification, so add it now
					qual.QualificationRequirements.Add(new DTO.Registry.QualificationRequirement()
					{
						QualificationID = qual.QualificationID,
						RequirementID = newReq.RequirementID,
						LinkUID = Guid.NewGuid(),
						DisplayOrder = displayOrder,
						CreatedDate = DateTime.UtcNow,
						DTOState = DTO.State.Created,
					});
				}
				else
				{
					//already in, just need to update display order
					if (existingRequirement.DisplayOrder != displayOrder)
					{
						existingRequirement.UpdatedDate = DateTime.Now;
						existingRequirement.DTOState = DTO.State.Updated;
						existingRequirement.DisplayOrder = displayOrder;
					}
				}

				displayOrder++;
			}

			//the API will use the order of the items and re-set their display order, so update the items
			qual.QualificationRequirements = qual.QualificationRequirements.OrderBy(o => o.DisplayOrder).ToList();


			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/qualification";
					string httpMethod = "PUT";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Registry.Qualification>(qual));

					DTO.ServiceResult<DTO.Registry.Qualification> result = Deserialize<DTO.ServiceResult<DTO.Registry.Qualification>>(resultString);

					if (result.IsSuccess() == true)
					{
						DTO.Registry.Qualification updatedQual = GetQualificationById(result.ObjectIdentity.ObjectID.Value);

						QualFacade q = new QualFacade(updatedQual, null);

						return q;
					}

				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}

			return null;
		}

		#endregion

		#region Requirements

		public List<ReqFacade> GetRequirements(int contextNodeId)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.ContextNodeID = contextNodeId;

					string uri = _baseUrl + "/requirements";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

					DTO.Registry.Requirement[] results = Deserialize<DTO.Registry.Requirement[]>(resultString);

					if (results.Length == 0)
						return new List<ReqFacade>();
					else
					{
						List<ReqFacade> requirements = new List<ReqFacade>();

						foreach (DTO.Registry.Requirement requirement in results)
						{
							var parentNode = _nodeCache.Where(n => n.NodeID == requirement.NodeID).FirstOrDefault();

							int parentId = (parentNode != null) ? (parentNode.ParentID) : (contextNodeId);

							ReqFacade r = new ReqFacade(requirement, parentId);

							_reqCache.Add(r);

							requirements.Add(r);
						}

						return requirements;

					}
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return null;
			}
		}

		public ReqFacade GetRequirement(int parentId, string parentName, string name)
		{
			var cachedObj = _reqCache.Where(n => n.ParentID == parentId && n.Name == name).FirstOrDefault();
			if (cachedObj != null)
				return cachedObj;
			else
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					try
					{
						NodeFacade registry = GetRegistry(parentId, parentName);
						if (registry == null)
							throw new Exception("Unable to find registry under " + parentId.ToString() + " named " + name);


						APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
						qs.Name = name;
						qs.NodeID = registry.NodeID;

						string uri = _baseUrl + "/requirements";
						string httpMethod = "POST";
						DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

						string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

						DTO.Registry.Requirement[] results = Deserialize<DTO.Registry.Requirement[]>(resultString);

						if (results.Length == 0)
							return null;
						else
						{
							DTO.Registry.Requirement requirement = results.Where(r => r.Name.ToLower() == name.ToLower()).SingleOrDefault();

							if (requirement == null && results.Length == 1)
								requirement = results.First();

							if (requirement != null)
							{
								ReqFacade r = new ReqFacade(requirement, parentId);

								_reqCache.Add(r);

								return r;
							}
							else
							{
								return null;
							}

						}
					}
					catch (Exception ex)
					{
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
					}
					return null;
				}
			}
		}

		private DTO.Registry.Requirement GetRequirementById(int requirementId)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/requirement/" + requirementId.ToString();
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);

					if (String.IsNullOrEmpty(resultString))
						return null;
					else
					{
						DTO.Registry.Requirement requirement = Deserialize<DTO.Registry.Requirement>(resultString);

						return requirement;
					}
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return null;
			}
		}

		public ReqFacade CreateRequirement(int parentId, string parentName, string name, string externalID)
		{
			try
			{
				NodeFacade registryNode = GetNode(parentId, parentName, DTO.Directory.NodeTypes.Storage, DTO.Directory.NodeSubTypes.Registry);

				if (registryNode == null)
					throw new Exception("Error creating requirement - Unable to find registry with parentId '" + parentId + "' and name '" + name + "'");

				DTO.Registry.Requirement newObj = new DTO.Registry.Requirement();
				newObj.RegistryID = registryNode.ObjectID;
				newObj.RequirementID = -1;
				newObj.Name = name;
				newObj.CompleteItemsModeID = DTO.Registry.CompleteItemsModes.AllItems;
				newObj.CompleteItemsOrderModeID = DTO.Registry.CompleteItemsOrderModes.AnyOrder;
				newObj.DTOState = DTO.State.Created;
				newObj.CreatedDate = DateTime.Now;
				newObj.RequirementActivities = new List<DTO.Registry.RequirementActivity>();
				newObj.RequirementDetail = new DTO.Registry.RequirementDetail();
				newObj.RequirementDetail.DTOState = DTO.State.Created;
				newObj.RequirementCompetentUsers = new List<DTO.Registry.RequirementCompetentUser>();
				newObj.ExternalID = externalID;

				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + "/requirement";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Registry.Requirement>(newObj));
					DTO.ServiceResult<DTO.Registry.Requirement> result = Deserialize<DTO.ServiceResult<DTO.Registry.Requirement>>(resultString);

					if (result.IsSuccess() == true)
					{
						ReqFacade q = new ReqFacade();
						q.RegistryID = registryNode.ObjectID;
						q.RequirementID = result.ObjectIdentity.ObjectID.Value;
						q.Name = result.ObjectIdentity.Name;
						q.ActivityIds = new List<int>();
						return q;
					}
					else
					{
						throw new Exception("Error creating Requirement '" + name + "': " + result.Message);
					}
				}

			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

        // BJM on 5/12/2020 - Original UpdateRequirement just had parameters for requirementId and requirementActivities.  Since we need to be able to update name,
        // added the below overload.
        public ReqFacade UpdateRequirement(int requirementId, List<ActivityFacade> requirementActivities)
        {
            return UpdateRequirement(requirementId, requirementActivities, null);
        }

        public ReqFacade UpdateRequirement(int requirementId, List<ActivityFacade> requirementActivities, String name)
        {
            DTO.Registry.Requirement req = GetRequirementById(requirementId);

            if (req == null)
                throw new Exception("Error updating requirement, unable to find existing requirement: " + requirementId.ToString());

            if (!String.IsNullOrEmpty(name)) req.Name = name;

            req.DTOState = DTO.State.Updated;

            List<int> newActivityIds = requirementActivities.Select(o => o.ActivityID).ToList();
            List<int> oldActivityIds = req.RequirementActivities.OrderBy(o => o.DisplayOrder).Select(o => o.ActivityID).ToList();

            // BJM on 5/12/2020 - Since I added logic to update name via this method, I removed the IF below.
            //if (newActivityIds.SequenceEqual(oldActivityIds))
            //{
            //    return new ReqFacade(req, null);
            //}

            //find any current activities not in the new activity list and remove them
            for (int i = req.RequirementActivities.Count - 1; i >= 0; i--)
            {
                DTO.Registry.RequirementActivity ra = req.RequirementActivities[i];
                if (!newActivityIds.Contains(ra.ActivityID))
                    req.RequirementActivities.Remove(ra);

            }

            //go through each activity and see if it needs to be added or updated
            int displayOrder = 1;
            for (int i = 0; i < requirementActivities.Count; i++)
            {
                ActivityFacade newAct = requirementActivities[i];

                DTO.Registry.RequirementActivity existingActivity = req.RequirementActivities.Where(o => o.ActivityID == newAct.ActivityID).SingleOrDefault();
                if (existingActivity == null)
                {
                    //not already in requirement, so add it now
                    req.RequirementActivities.Add(new DTO.Registry.RequirementActivity()
                    {
                        RequirementID = req.RequirementID,
                        ActivityID = newAct.ActivityID,
                        LinkUID = Guid.NewGuid(),
                        DisplayOrder = displayOrder,
                        CreatedDate = DateTime.UtcNow,
                        DTOState = DTO.State.Created,
                    });
                }
                else
                {
                    //already in, just need to update display order
                    existingActivity.UpdatedDate = DateTime.Now;
                    existingActivity.DTOState = DTO.State.Updated;
                    existingActivity.DisplayOrder = displayOrder;
                }

                displayOrder++;
            }


            //NOTE: the "displayorder" field is not currently used by the api method, so need to put them in the desired order
            req.RequirementActivities = req.RequirementActivities.OrderBy(o => o.DisplayOrder).ToList();

            using (System.Net.WebClient wc = new System.Net.WebClient())
            {
                try
                {
                    string uri = _baseUrl + "/requirement";
                    string httpMethod = "PUT";
                    DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

                    string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Registry.Requirement>(req));

                    DTO.ServiceResult<DTO.Registry.Requirement> result = Deserialize<DTO.ServiceResult<DTO.Registry.Requirement>>(resultString);

                    if (result.IsSuccess() == true)
                    {
                        DTO.Registry.Requirement updatedReq = GetRequirementById(result.ObjectIdentity.ObjectID.Value);

                        return new ReqFacade(updatedReq, null);
                    }
                }
                catch (Exception ex)
                {
                    log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
                }
            }
            return null;
		}
		#endregion

		#region Activities and Files

		public ActivityFacade GetActivity(int parentId, string parentName, string name, string crmProductId, string culture)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					NodeFacade registry = GetRegistry(parentId, parentName);
					if (registry == null)
						throw new Exception("Unable to find registry under " + parentId.ToString() + " named " + parentName);

					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.Name = name;
					qs.NodeID = registry.NodeID;

					string uri = _baseUrl + "/activities";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

					DTO.Registry.Activity[] results = Deserialize<DTO.Registry.Activity[]>(resultString);

					if (results.Length == 0)
						return null;
					else
					{
						DTO.Registry.Activity activity = results.Where(r => r.Name == name
																																&& r.CurrentVersion != null
																																&& r.CurrentVersion.File != null
																																&& r.CurrentVersion.File.ExternalID == crmProductId.ToString()
																																&& ((culture == "EN" && (r.CurrentVersion.File.Culture == "EN" || r.CurrentVersion.File.Culture == null || r.CurrentVersion.File.Culture == "")) || (culture != null && r.CurrentVersion.File.Culture == culture))
																														).SingleOrDefault();

						if (activity != null)
						{

							ActivityFacade a = new ActivityFacade();
							a.RegistryID = activity.RegistryID;
							a.Name = activity.Name;
							a.ActivityID = activity.ActivityID;
							a.ParentLookupID = parentId;
							if (activity.CurrentVersion != null)
							{
								if (activity.CurrentVersion.Duration.HasValue)
									a.Duration = activity.CurrentVersion.Duration.Value;
								if (!String.IsNullOrEmpty(activity.CurrentVersion.Description))
									a.Description = activity.CurrentVersion.Description;

								if (activity.CurrentVersion.File != null)
								{
									//set fields that should correspond to the ProductID and CurrentVersion from CRM
									a.ExternalID = activity.CurrentVersion.File.ExternalID;
									a.ExternalVersion = activity.CurrentVersion.File.ExternalVersion;
									a.Culture = activity.CurrentVersion.File.Culture ?? "EN";
									a.FileID = activity.CurrentVersion.File.FileID;
									if (activity.CurrentVersion.FileVersionUID.HasValue)
										a.FileVersionUID = activity.CurrentVersion.FileVersionUID.Value;
								}
							}




							return a;
						}
						else
						{
							return null;
						}

					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}

		public ActivityFacade GetActivityByContext(int contextNodeId, string name, string crmProductId, string culture)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.Name = name;
					qs.ContextNodeID = contextNodeId;

					string uri = _baseUrl + "/activities";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					//Console.WriteLine("<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

					//Console.WriteLine(resultString);

					DTO.Registry.Activity[] results = Deserialize<DTO.Registry.Activity[]>(resultString);

					if (results.Length == 0)
						return null;
					else
					{
						DTO.Registry.Activity activity = results.Where(r => r.Name == name
																																&& r.CurrentVersion != null
																																&& r.CurrentVersion.File != null
																																&& r.CurrentVersion.File.ExternalID == crmProductId.ToString()
																																&& ((culture == "EN" && (r.CurrentVersion.File.Culture == "EN" || r.CurrentVersion.File.Culture == null || r.CurrentVersion.File.Culture == "")) || (culture != null && r.CurrentVersion.File.Culture == culture))
																														).FirstOrDefault();

						if (activity != null)
						{

							ActivityFacade a = new ActivityFacade();
							a.RegistryID = activity.RegistryID;
							a.Name = activity.Name;
							a.ActivityID = activity.ActivityID;
							a.ParentLookupID = contextNodeId;
							if (activity.CurrentVersion != null)
							{
								if (activity.CurrentVersion.Duration.HasValue)
									a.Duration = activity.CurrentVersion.Duration.Value;
								if (!String.IsNullOrEmpty(activity.CurrentVersion.Description))
									a.Description = activity.CurrentVersion.Description;

								if (activity.CurrentVersion.File != null)
								{
									//set fields that should correspond to the ProductID and CurrentVersion from CRM
									a.ExternalID = activity.CurrentVersion.File.ExternalID;
									a.ExternalVersion = activity.CurrentVersion.File.ExternalVersion;
									a.Culture = activity.CurrentVersion.File.Culture ?? "EN";
									a.FileID = activity.CurrentVersion.File.FileID;
									if (activity.CurrentVersion.FileVersionUID.HasValue)
										a.FileVersionUID = activity.CurrentVersion.FileVersionUID.Value;
								}
							}




							return a;
						}
						else
						{
							return null;
						}

					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
					Console.WriteLine(wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
					Console.WriteLine(ex);
				}
			}
			return null;
		}

		public List<ActivityFacade> GetActivities(int parentId, string parentName)
		{
			List<ActivityFacade> objects = new List<ActivityFacade>();

			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					NodeFacade registry = GetRegistry(parentId, parentName);
					if (registry == null)
						throw new Exception("Unable to find registry under " + parentId.ToString());

					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.ContextNodeID = parentId;
					qs.MaxResults = 5000;

					string uri = _baseUrl + "/activities";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

					DTO.Registry.Activity[] results = Deserialize<DTO.Registry.Activity[]>(resultString);

					foreach (DTO.Registry.Activity result in results.Where(r => r.ActivityType != null && r.ActivityType.TypeID == 2)) //2=SCORM_CBT
					{
						ActivityFacade a = new ActivityFacade();
						a.RegistryID = result.RegistryID;
						a.Name = result.Name;
						a.ActivityID = result.ActivityID;
						a.ParentLookupID = registry.ParentID;
						if (result.CurrentVersion != null)
						{
							if (result.CurrentVersion.Duration.HasValue)
								a.Duration = result.CurrentVersion.Duration.Value;
							if (!String.IsNullOrEmpty(result.CurrentVersion.Description))
								a.Description = result.CurrentVersion.Description;

							if (result.CurrentVersion.File != null)
							{
								//set fields that should correspond to the ProductID and CurrentVersion from CRM
								a.ExternalID = result.CurrentVersion.File.ExternalID;
								a.ExternalVersion = result.CurrentVersion.File.ExternalVersion;
								a.Culture = result.CurrentVersion.File.Culture ?? "EN";
								a.FileID = result.CurrentVersion.File.FileID;
								if (result.CurrentVersion.FileVersionUID.HasValue)
									a.FileVersionUID = result.CurrentVersion.FileVersionUID.Value;//.File.CurrentVersion.FileVersionUID;
							}
						}



						objects.Add(a);

					}

				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return objects;
		}


		public ActivityFacade AddAICCFileActivity(int parentRepositoryId, string parentRepositoryName,
											int parentRegistryId, string parentRegistryName,
											string activityName, string fileName,
											string description,
											string aiccLaunchUrl, string currentVersion, string externalId, int duration, string culture, bool isMobile)
		{

			try
			{
				//lookup repository
				NodeFacade repository = GetRepository(parentRepositoryId, parentRepositoryName);
				if (repository == null)
					throw new Exception("Unable to find repository under " + parentRepositoryId.ToString() + " named " + parentRepositoryName);

				NodeFacade registry = GetRegistry(parentRegistryId, parentRegistryName);
				if (registry == null)
					throw new Exception("Unable to find registry under " + parentRegistryId.ToString() + " named " + parentRegistryName);


				//create file DTO and set properties
				DTO.Repository.File file = new DTO.Repository.File();
				file.RepositoryID = repository.ObjectID;
				file.Name = (fileName.Length > 128) ? (fileName.Substring(0, 127)) : (fileName);
				file.Description = (description.Length > 512) ? (description.Substring(0, 511)) : (description);
				file.Description = file.Description.Replace("&#39;", "'").Replace("&#39", "'").Replace("&#3", "'");
				//ensure Desc is still less than 512
				file.OwnerPrincipleID = 0;
				file.Field2 = externalId;
				file.Field3 = currentVersion;
				file.ExternalID = externalId;
				file.ExternalVersion = currentVersion;
				file.Culture = culture;
				file.TypeID = (int)DTO.Repository.FileTypes.AICC;
				file.LicenseTypeID = (int)DTO.Repository.RepositoryFileLicenseTypes.Commercial;
				if (aiccLaunchUrl.Contains("?"))
					file.URL = aiccLaunchUrl + "&aicc_sid=[SID]&aicc_url=[URL]";
				else
					file.URL = aiccLaunchUrl + "?aicc_sid=[SID]&aicc_url=[URL]";

				DTO.ServiceResult<DTO.Repository.File> fileCreationResult = CreateFile(file);

				if (fileCreationResult != null && fileCreationResult.IsSuccess())
				{

					FileFacade newFile = GetFileByExternalID(externalId);
					if (newFile != null)
					{
						DTO.Registry.Activity activity = new DTO.Registry.Activity();
						activity.TypeID = DTO.Registry.ActivityTypes.SCORM_CBT;
						activity.Name = (activityName.Length > 128) ? (activityName.Substring(0, 127)) : (activityName);
						activity.IsMobileCompatible = isMobile;
						activity.CurrentVersion = new DTO.Registry.ActivityVersion()
						{
							FileVersionUID = newFile.FileVersionUID,
							FileID = fileCreationResult.ObjectIdentity.ObjectID,
							Height = 632,
							Width = 1002,
							Duration = duration,
							Description = description
						};

						activity.RegistryID = registry.ObjectID;
						//activity.LaunchData = "passingScore=80";

						var activityResult = CreateActivity(activity);

						if (activityResult != null && activityResult.IsSuccess())
						{
							ActivityFacade newActivity = new ActivityFacade();
							newActivity.ActivityID = activityResult.ObjectIdentity.ObjectID.Value;
							newActivity.Name = activityResult.ObjectIdentity.Name;
							newActivity.RegistryID = registry.ObjectID;
							newActivity.ExternalID = externalId;
							newActivity.ExternalVersion = currentVersion;
							newActivity.FileVersionUID = newFile.FileVersionUID;
							newActivity.FileID = fileCreationResult.ObjectIdentity.ObjectID.Value;
							newActivity.Duration = duration;
							newActivity.Description = description;
							newActivity.Culture = file.Culture;
							return newActivity;
						}

					}
					else
					{
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + " - GetFileByExternalID Result is Null");
					}
				}
				else
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + " - fileCreationResult Result is NOT Success");
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public FileFacade GetFileByExternalID(string externalId)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + String.Format("/files?cid={0}", externalId);
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);

					if (String.IsNullOrEmpty(resultString))
						return null;
					else
					{
						DTO.Repository.File[] results = Deserialize<DTO.Repository.File[]>(resultString);

						if (results.Length > 0)
						{
							DTO.Repository.File result = results.First();

							FileFacade file = new FileFacade();
							file.FileID = result.FileID;
							file.RepositoryID = result.RepositoryID;
							file.Name = result.Name;
							file.FileVersionUID = result.CurrentVersion.FileVersionUID;

							return file;
						}
						else
							return null;
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return null;
			}
		}

		public DTO.Repository.File GetFileByFileUID(string fileUID)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + String.Format("/file?uid={0}", fileUID);
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);
					string resultString = wc.DownloadString(uri);
					if (String.IsNullOrEmpty(resultString))
						return null;
					else
					{
						return Deserialize<DTO.Repository.File>(resultString);
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return null;
			}
		}

		public List<FileFacade> GetFiles(int parentId)
		{
			List<FileFacade> objects = new List<FileFacade>();

			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.ContextNodeID = parentId;
					qs.MaxResults = 5000;

					string uri = _baseUrl + "/files";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);
					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));
					DTO.Repository.File[] results = Deserialize<DTO.Repository.File[]>(resultString);

					foreach (DTO.Repository.File result in results)
					{
						FileFacade file = new FileFacade();
						file.FileID = result.FileID;
						file.FileUID = result.FileUID;
						file.RepositoryID = result.RepositoryID;
						file.Name = result.Name;
						file.FileVersionUID = result.CurrentVersion.FileVersionUID;
						file.ExternalID = result.ExternalID;
						file.Culture = result.Culture;
						file.ExternalVersion = result.ExternalVersion;

						objects.Add(file);
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return objects;
			}
		}
		public ActivityFacade AddFileActivity(int parentRepositoryId, string parentRepositoryName,
																					int parentRegistryId, string parentRegistryName,
																					string activityName, string fileName,
																					string description,
																					string courseZipPath, string currentVersion,
																					string crmProductId, int duration, string culture,
																					LaunchParameters launchParams)
		{

			try
			{
				//lookup repository
				NodeFacade repository = GetRepository(parentRepositoryId, parentRepositoryName);
				if (repository == null)
					throw new Exception("Unable to find repository under " + parentRepositoryId.ToString() + " named " + parentRepositoryName);

				NodeFacade registry = GetRegistry(parentRegistryId, parentRegistryName);
				if (registry == null)
					throw new Exception("Unable to find registry under " + parentRegistryId.ToString() + " named " + parentRegistryName);


				//create file DTO and set properties
				DTO.Repository.File file = new DTO.Repository.File();
				file.RepositoryID = repository.ObjectID;
				file.Name = fileName;
				file.Description = (description.Length > 512) ? (description.Substring(0, 511)) : (description);
				file.Description = file.Description.Replace("&#39;", "'").Replace("&#39", "'").Replace("&#3", "'");
				//ensure Desc is still less than 512
				file.OwnerPrincipleID = 0; //change to content_api_user?
				file.Field2 = crmProductId.ToString();
				file.Field3 = currentVersion;
				file.ExternalID = crmProductId.ToString();
				file.ExternalVersion = currentVersion;
				file.Culture = culture;
				file.TypeID = (int)DTO.Repository.FileTypes.SCORM;
				file.LicenseTypeID = (int)DTO.Repository.RepositoryFileLicenseTypes.Commercial;

				DTO.ServiceResult<DTO.Repository.File> fileCreationResult = CreateFile(file);

				if (fileCreationResult != null && fileCreationResult.IsSuccess())
				{
					DTO.Repository.File newFile = fileCreationResult.Object;
					int newFileId = fileCreationResult.ObjectIdentity.ObjectID.Value;

					APIDTO.UpdatedFile updatedFileSpec = new APIDTO.UpdatedFile()
					{
						CID = file.ExternalID,
						Version = file.ExternalVersion,
						Url = courseZipPath
					};

					var uploadResult = UploadFile(newFileId, updatedFileSpec);

					if (uploadResult != null && uploadResult.IsSuccess())
					{
						DTO.Registry.Activity activity = new DTO.Registry.Activity();
						activity.TypeID = DTO.Registry.ActivityTypes.SCORM_CBT;
						activity.Name = activityName;
						activity.IsMobileCompatible = true;
						activity.CurrentVersion = new DTO.Registry.ActivityVersion()
						{
							FileVersionUID = uploadResult.Object.CurrentVersion.FileVersionUID,
							FileID = uploadResult.Object.FileID,
							Height = 632,
							Width = 1002,
							Duration = duration,
							Description = description
						};

						if (launchParams != null && launchParams.HasValues)
						{
							activity.LaunchData = launchParams.ToString();//"playMode=standardOnly&passingScore=80";
						}



						activity.RegistryID = registry.ObjectID;
						//activity.LaunchData = "passingScore=80";

						var activityResult = CreateActivity(activity);

						if (activityResult != null && activityResult.IsSuccess())
						{
							ActivityFacade newActivity = new ActivityFacade();
							newActivity.ActivityID = activityResult.ObjectIdentity.ObjectID.Value;
							newActivity.Name = activityResult.ObjectIdentity.Name;
							newActivity.RegistryID = registry.ObjectID;
							newActivity.ExternalID = crmProductId.ToString();
							newActivity.ExternalVersion = currentVersion;
							newActivity.FileVersionUID = uploadResult.Object.CurrentVersion.FileVersionUID;
							newActivity.FileID = uploadResult.Object.FileID;
							newActivity.Duration = duration;
							newActivity.Description = description;
							newActivity.Culture = file.Culture;
							//newActivity.laun
							return newActivity;
						}
					}
				}
				else
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + " - fileCreationResult Result is NOT Success");
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public ActivityFacade UpdateFileActivity(int parentRepositoryId, string parentRepositoryName,
																			int parentRegistryId, string parentRegistryName,
																			string activityName,
																			string description,
																			int activityId, int fileId,
																			string courseZipPath, string currentVersion,
																			string crmProductId, int duration, string culture)
		{

			try
			{
				APIDTO.UpdatedFile updatedFileSpec = new APIDTO.UpdatedFile()
				{
					CID = crmProductId.ToString(),
					Version = currentVersion,
					Url = courseZipPath
				};

				var uploadResult = UploadFile(fileId, updatedFileSpec);

				if (uploadResult != null && uploadResult.IsSuccess())
				{
					var updateActivityResult = UpdateActivity(activityId, fileId, uploadResult.Object.CurrentVersion.FileVersionUID, duration, description);
					return GetActivity(parentRegistryId, parentRegistryName, activityName, crmProductId, culture);
				}

			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public DTO.ServiceResult<DTO.Repository.File> UpdateFile(DTO.Repository.File file)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/file";
					string httpMethod = "PUT";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Repository.File>(file));

					DTO.ServiceResult<DTO.Repository.File> result = Deserialize<DTO.ServiceResult<DTO.Repository.File>>(resultString);

					if (result.IsSuccess() == true)
					{
						return result;
					}

				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}

		public DTO.ServiceResult<DTO.Repository.File> UpdateFileWithBase64Encoding(DTO.Repository.File file)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/fileencoded";
					string httpMethod = "PUT";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Repository.File>(file));

					DTO.ServiceResult<DTO.Repository.File> result = Deserialize<DTO.ServiceResult<DTO.Repository.File>>(resultString);

					if (result.IsSuccess() == true)
					{
						return result;
					}

				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}
		public DTO.ServiceResult<DTO.Repository.File> CreateFile(DTO.Repository.File file)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/file";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);
					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Repository.File>(file));

					DTO.ServiceResult<DTO.Repository.File> result = Deserialize<DTO.ServiceResult<DTO.Repository.File>>(resultString);

					if (result.IsSuccess() == true)
					{
						return result;
					}
					else
					{
						string msg = result.Message;
						Console.WriteLine("Error in CreateFile: " + msg);
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, new Exception(msg));
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
					Console.WriteLine("Error (WebException) in CreateFile: " + convErrorMessage+"\r\n"+wex.ToString());
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
					Console.WriteLine("Error (Exception) in CreateFile: " + ex.ToString());
				}
				return null;
			}
		}

		public DTO.ServiceResult<DTO.Repository.File> CreateFileWithBase64Encoding(DTO.Repository.File file)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/fileencoded";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);
					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Repository.File>(file));

					DTO.ServiceResult<DTO.Repository.File> result = Deserialize<DTO.ServiceResult<DTO.Repository.File>>(resultString);

					if (result.IsSuccess() == true)
					{
						return result;
					}
					else
					{
						string msg = result.Message;
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, new Exception(msg));
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return null;
			}
		}

		public DTO.ServiceResult<DTO.Repository.File> UploadFile(int fileId, APIDTO.UpdatedFile updatedFileSpec)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/file/" + fileId.ToString() + "/upload";
					string httpMethod = "PUT";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.UpdatedFile>(updatedFileSpec));

					DTO.ServiceResult<DTO.Repository.File> result = Deserialize<DTO.ServiceResult<DTO.Repository.File>>(resultString);

					if (result.IsSuccess() == true)
					{
						return result;
					}
					else
					{
						string msg = result.Message;
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, new Exception(msg));
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}

		public ActivityFacade CreateLinkedActivity(ActivityFacade originalActivity, NodeFacade targetNode)
		{
			NodeFacade registry = GetRegistry(targetNode.NodeID, targetNode.Name);
			if (registry == null)
				throw new Exception("Unable to find registry under " + targetNode.NodeID.ToString() + " named " + targetNode.Name);


			DTO.Registry.Activity activity = new DTO.Registry.Activity();
			activity.TypeID = DTO.Registry.ActivityTypes.SCORM_CBT;
			activity.Name = originalActivity.Name;
			activity.IsMobileCompatible = true;
			activity.CurrentVersion = new DTO.Registry.ActivityVersion()
			{
				FileVersionUID = originalActivity.FileVersionUID,
				FileID = originalActivity.FileID,
				Height = 632,
				Width = 1002,
				Duration = originalActivity.Duration,
				Description = originalActivity.Description
			};

			activity.RegistryID = registry.ObjectID;
			//activity.LaunchData = "passingScore=80";

			var activityResult = CreateActivity(activity);

			if (activityResult != null && activityResult.IsSuccess())
			{
				ActivityFacade newActivity = new ActivityFacade();
				newActivity.ActivityID = activityResult.ObjectIdentity.ObjectID.Value;
				newActivity.Name = activityResult.ObjectIdentity.Name;
				newActivity.RegistryID = registry.ObjectID;
				//newActivity.ExternalID = crmProductId.ToString();
				//newActivity.ExternalVersion = currentVersion;
				newActivity.FileVersionUID = originalActivity.FileVersionUID;
				newActivity.FileID = originalActivity.FileID;
				newActivity.Duration = originalActivity.Duration;
				newActivity.Description = originalActivity.Description;
				newActivity.Culture = originalActivity.Culture;
				return newActivity;
			}

			return null;
		}

		public DTO.ServiceResult<DTO.Registry.Activity> CreateActivity(DTO.Registry.Activity activity)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/activity";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Registry.Activity>(activity));

					DTO.ServiceResult<DTO.Registry.Activity> result = Deserialize<DTO.ServiceResult<DTO.Registry.Activity>>(resultString);

					if (result.IsSuccess() == true)
					{
						return result;
					}
					else
					{
						string msg = result.Message;
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, new Exception(msg));
					}

				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}

		public DTO.ServiceResult<DTO.Registry.Activity> CreateActivityWithBase64Encoding(DTO.Registry.Activity activity)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/activityencoded";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Registry.Activity>(activity));

					DTO.ServiceResult<DTO.Registry.Activity> result = Deserialize<DTO.ServiceResult<DTO.Registry.Activity>>(resultString);

					if (result.IsSuccess() == true)
					{
						return result;
					}
					else
					{
						string msg = result.Message;
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, new Exception(msg));
					}

				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}
		public DTO.ServiceResult<DTO.Registry.Activity> UpdateActivity(int activityId, int fileId, Guid fileVersionUID, int duration, string description)
		{
			return UpdateActivity(activityId, fileId, fileVersionUID, duration, description, null);
		}

		public DTO.ServiceResult<DTO.Registry.Activity> SunsetActivity(int activityId, int fileId, Guid fileVersionUID, DateTime sunsetDate)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.ObjectID = activityId;

					string uri = _baseUrl + "/activities";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

					DTO.Registry.Activity[] results = Deserialize<DTO.Registry.Activity[]>(resultString);

					if (results.Length == 0 || results.SingleOrDefault() == null)
						throw new Exception("Unable to find activity by id: " + activityId.ToString());
					else
					{
						DTO.Registry.Activity activity = results.SingleOrDefault();

						string sunsetNameString = $" (Sunset on {sunsetDate.ToShortDateString()})";

						//ensure added text doesn't exceed max length of Activity.Name column						
						int maxNameLength = 256;
						int diff = maxNameLength - activity.Name.Length - sunsetNameString.Length;
						if (diff >= 0)
						{
							activity.Name = activity.Name + sunsetNameString;
						}
						else
						{
							string truncatedActivityName = activity.Name.Substring(0, activity.Name.Length + diff);
							activity.Name = truncatedActivityName + sunsetNameString;
						}

						activity.Field4 = $"PreviousFileID:{activity.CurrentVersion.File.FileID}";
						activity.CurrentVersion = new DTO.Registry.ActivityVersion()
						{
							FileVersionUID = fileVersionUID,
							FileID = fileId,
							Height = 632,
							Width = 1002,
							Duration = 1,
							Description = "This course has been sunset (discontinued) and is no longer available. Please contact your Administrator or support@convergencetraining.com if you have any questions.",
						};

						var updateActivityResult = UpdateActivityVersion(activity);
						return updateActivityResult;
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}


		public DTO.ServiceResult<DTO.Registry.Activity> UpdateActivity(int activityId, int fileId, Guid fileVersionUID, int duration, string description, string name)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.ObjectID = activityId;

					string uri = _baseUrl + "/activities";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

					DTO.Registry.Activity[] results = Deserialize<DTO.Registry.Activity[]>(resultString);

					if (results.Length == 0 || results.SingleOrDefault() == null)
						throw new Exception("Unable to find activity by id: " + activityId.ToString());
					else
					{
						DTO.Registry.Activity activity = results.SingleOrDefault();

						// BJM on 4/8/2019 - Added below line for content sync if activity name was changed.
						if (!String.IsNullOrEmpty(name)) activity.Name = name;

						// KDV on 5/15/2020 - Added below to make sure the description doesn't go blank
						if (String.IsNullOrWhiteSpace(description) && !String.IsNullOrWhiteSpace(activity.CurrentVersion.Description))
							description = activity.CurrentVersion.Description;

						activity.CurrentVersion = new DTO.Registry.ActivityVersion()
						{
							FileVersionUID = fileVersionUID,
							FileID = fileId,
							Height = 632,
							Width = 1002,
							Duration = duration,
							Description = description
						};

						//TODO: Add check/udpate of launch params
						/*
						if (launchParams != null && launchParams.HasValues)
						{
							activity.LaunchData = launchParams.ToString();//"playMode=standardOnly&passingScore=80";
						}*/

						var updateActivityResult = UpdateActivityVersion(activity);
						return updateActivityResult;
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}

		public DTO.ServiceResult<DTO.Registry.Activity> UpdateActivityVersion(DTO.Registry.Activity activity)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/activity";
					string httpMethod = "PUT";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Registry.Activity>(activity));

					DTO.ServiceResult<DTO.Registry.Activity> result = Deserialize<DTO.ServiceResult<DTO.Registry.Activity>>(resultString);

					return result;

				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}

		public DTO.ServiceResult<DTO.Registry.Activity> UpdateActivityWithBase64Encoding(int activityId, int fileId, Guid fileVersionUID, int duration, string description, string name)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					APIDTO.SimpleQuerySpec qs = new APIDTO.SimpleQuerySpec();
					qs.ObjectID = activityId;

					string uri = _baseUrl + "/activities";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<APIDTO.SimpleQuerySpec>(qs));

					DTO.Registry.Activity[] results = Deserialize<DTO.Registry.Activity[]>(resultString);

					if (results.Length == 0 || results.SingleOrDefault() == null)
						throw new Exception("Unable to find activity by id: " + activityId.ToString());
					else
					{
						DTO.Registry.Activity activity = results.SingleOrDefault();

						// BJM on 4/8/2019 - Added below line for content sync if activity name was changed.
						if (!String.IsNullOrEmpty(name)) activity.Name = name;

						activity.CurrentVersion = new DTO.Registry.ActivityVersion()
						{
							FileVersionUID = fileVersionUID,
							FileID = fileId,
							Height = 632,
							Width = 1002,
							Duration = duration,
							Description = description,
							//Description = "API Test",
						};

						//TODO: Add check/udpate of launch params
						/*
if (launchParams != null && launchParams.HasValues)
{
	activity.LaunchData = launchParams.ToString();//"playMode=standardOnly&passingScore=80";
}*/

						var updateActivityResult = UpdateActivityVersionWithBase64Encoding(activity);
						return updateActivityResult;
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}

		public DTO.ServiceResult<DTO.Registry.Activity> UpdateActivityVersionWithBase64Encoding(DTO.Registry.Activity activity)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + "/activityencoded";
					string httpMethod = "PUT";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Registry.Activity>(activity));

					DTO.ServiceResult<DTO.Registry.Activity> result = Deserialize<DTO.ServiceResult<DTO.Registry.Activity>>(resultString);

					return result;

				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
			}
			return null;
		}

		#endregion

		#region Thumbnails

		public List<ThumbnailFacade> GetThumbnails(string type, int contextNodeId)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + String.Format("/thumbnails/{0}?ContextNodeID={1}", type, contextNodeId);
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);

					if (String.IsNullOrEmpty(resultString))
						return new List<ThumbnailFacade>();
					else
					{
						List<ThumbnailFacade> thumbs = new List<ThumbnailFacade>();

						DTO.Registry.TrainingImage[] results = Deserialize<DTO.Registry.TrainingImage[]>(resultString);

						foreach (DTO.Registry.TrainingImage result in results)
						{
							ThumbnailFacade thumb = new ThumbnailFacade();
							thumb.ActivityID = result.ActivityID.Value;
							thumb.Image = result.Image;

							_thumbCache.Add(thumb);
							thumbs.Add(thumb);
						}

						return thumbs;
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return null;
			}
		}

		public string GetActivityImage(int activityId)
		{
			var cachedObj = _thumbCache.Where(o => o.ActivityID == activityId).FirstOrDefault();

			if (cachedObj != null)
				return cachedObj.Image;
			else
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					try
					{
						string uri = _baseUrl + String.Format("/activity/{0}/thumbnail", activityId);
						string httpMethod = "GET";
						string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
						wc.Headers.Add("Authorization", authHeader);

						string resultString = wc.DownloadString(uri);

						if (!String.IsNullOrEmpty(resultString))
						{
							DTO.Registry.TrainingImage result = Deserialize<DTO.Registry.TrainingImage>(resultString);

							ThumbnailFacade thumb = new ThumbnailFacade();
							thumb.ActivityID = result.ActivityID.Value;
							thumb.Image = result.Image;

							_thumbCache.Add(thumb);

							return result.Image;
						}
					}
					catch (WebException wex)
					{
						string convErrorMessage = GetConvErrorMessage(wex.Response);
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
					}
					catch (Exception ex)
					{
						log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
					}
				}
			}



			return null;
		}

		public bool UploadActivityImage(int activityId, string thumbnailUrl)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + String.Format("/activity/{0}/thumbnail", activityId);
					string httpMethod = "PUT";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<string>(thumbnailUrl));

					DTO.ServiceResult<DTO.Registry.TrainingImage> result = Deserialize<DTO.ServiceResult<DTO.Registry.TrainingImage>>(resultString);

					if (result.IsSuccess() != true)
					{
						throw new Exception(result.Message);
					}

					return true;

				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return false;

			}
		}

		#endregion

		#region Assignments

		//TODO: Finish
		/*public List<APIDTO.TrainingPlanStatusItem> GetTrainingItemsForUser(Guid userUid)
    {
      try
      {

        using (System.Net.WebClient wc = new System.Net.WebClient())
        {
          string uri = _baseUrl + String.Format("/user/{0}/trainingstatus", userUid);

          string httpMethod = "GET";
          string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
          wc.Headers.Add("Authorization", authHeader);

          string resultString = wc.DownloadString(uri);

          List<APIDTO.TrainingPlanStatusItem> results = Deserialize<List<APIDTO.TrainingPlanStatusItem>>(resultString);
          return results;
        }

      }
      catch (WebException wex)
      {
        string convErrorMessage = GetConvErrorMessage(wex.Response);
        log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
      }
      catch (Exception ex)
      {
        //ex.Dump();
      }
      return null;
    }*/

		/// <summary>
		/// To get Qualification: lookupname = AttributeName, lookupvalue = AttributeValue
		/// To get User: userlookuptype can be username, userid, or externalid </summary>
		public bool AssignQualificationAdvanced(string lookupname, string lookupvalue, string userlookuptype, string userlookup, int daystolaunch, int daystocomplete)
		{
			try
			{

				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + "/advanced-assign-training?trainingtype=qualification";
					uri += "&lookupname=" + lookupname + "&lookupvalue=" + lookupvalue;
					uri += "&userlookuptype=" + userlookuptype + "&userlookup=" + userlookup;
					uri += "&daystolaunch=" + daystolaunch + "&daystocomplete=" + daystocomplete;
					//uri += "&maxcompletions=" + maxcompletions;
					uri += "&secretkey=" + _secretKey;

					string result = wc.DownloadString(uri);
					//result.Dump("Result");

					if (result.Contains("00101 SUCCESS") || result.Contains("Training directly assigned to User"))
					{
						return true;
					}
					else
					{
						return false;
					}
				}

			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return false;
		}


		public List<AssignmentFacade> GetAssignmentsByNodeUID(Guid nodeUid)
		{
			try
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + "/node/" + nodeUid.ToString() + "/training";
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);
					//Console.WriteLine(resultString);

					if (String.IsNullOrEmpty(resultString))
						return null;
					else
					{
						List<DTO.Registry.NodeAssignment> assignments = Deserialize<List<DTO.Registry.NodeAssignment>>(resultString);

						List<AssignmentFacade> results = new List<AssignmentFacade>();

						foreach (var assignment in assignments)
						{
							//Console.WriteLine(assignment);
							//Console.WriteLine("");

							results.Add(new AssignmentFacade()
							{
								NodeID = assignment.NodeID,
								Qualification = (assignment.Qualification != null) ? (new QualFacade()
								{
									QualificationID = assignment.Qualification.QualificationID,
									Name = assignment.Qualification.Name
								}) : (null),
								Activity = (assignment.Activity != null) ? (new ActivityFacade()
								{
									ActivityID = assignment.Activity.ActivityID,
									Name = assignment.Activity.Name
								}) : (null)
							});
						}

						return results;
					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public List<AssignmentFacade> AssignTrainingByNodeUID(Guid nodeUid, int? qualificationId, int? activityId)
		{
			DTO.Registry.NodeAssignment na = new DTO.Registry.NodeAssignment()
			{
				QualificationID = (qualificationId.HasValue) ? (qualificationId.Value) : ((int?)null),
				ActivityID = (activityId.HasValue) ? (activityId.Value) : ((int?)null)
			};
			return AssignTrainingByNodeUID(nodeUid, new List<DTO.Registry.NodeAssignment>() { na }.ToArray());
		}

		public List<AssignmentFacade> AssignTrainingByNodeUID(Guid nodeUid, DTO.Registry.NodeAssignment[] assignments)
		{
			try
			{
				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + "/node/" + nodeUid.ToString() + "/training";
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Registry.NodeAssignment[]>(assignments));
					DTO.Registry.NodeAssignment[] objects = Deserialize<DTO.Registry.NodeAssignment[]>(resultString);

					if (objects.Length == 0)
						return null;
					else
					{
						List<AssignmentFacade> results = new List<AssignmentFacade>();
						foreach (var assignment in objects)
						{
							results.Add(new AssignmentFacade()
							{
								NodeID = assignment.NodeID,
								Qualification = (assignment.QualificationID.HasValue) ? (new QualFacade()
								{
									QualificationID = assignment.QualificationID.Value
								}) : (null),
								Activity = (assignment.ActivityID.HasValue) ? (new ActivityFacade()
								{
									ActivityID = assignment.ActivityID.Value
								}) : (null)

							});
						}
						return results;
					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		#endregion

		#region Attributes

		public List<AttributeValueFacade> GetAttributeValues(int contextNodeId, string objectType, string attributeName)
		{

			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				string uri = _baseUrl + String.Format("/assets/{0}?AttributeName={1}&ContextNodeID={2}", objectType, attributeName, contextNodeId);
				string httpMethod = "GET";
				string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
				wc.Headers.Add("Authorization", authHeader);

				string resultString = wc.DownloadString(uri);
				DTO.Assets.Asset[] results = Deserialize<DTO.Assets.Asset[]>(resultString);

				if (results.Length == 0)
					return null;
				else
				{
					List<AttributeValueFacade> assetAttributeValues = new List<AttributeValueFacade>();

					foreach (DTO.Assets.Asset result in results)
					{
						int? objectId = result.AssetObjectID;

						var resultsExtended = result.AssetType.AssetAttributes.SelectMany(o => o.AttributeValues).ToList();
						foreach (DTO.Assets.AssetAttributeValue aav in resultsExtended)
						{
							AttributeValueFacade av = new AttributeValueFacade();
							av.ObjectType = objectType;
							av.ObjectID = objectId.Value.ToString();
							av.AttributeName = attributeName;
							av.AssetAttributeValueID = aav.AssetAttributeValueID;
							av.Value = aav.Value;
							if (aav.ValueID.HasValue)
								av.ValueID = aav.ValueID.Value;

							_attCache.Add(av);
							assetAttributeValues.Add(av);

						}

					}
					return assetAttributeValues;
				}
			}
		}

		public AttributeValueFacade GetAssetValue(string objectType, string objectId, string attributeName)
		{
			//TODO: Call "GetAssetValues" and return first object

			var cachedObj = _attCache.Where(o => o.ObjectType == objectType && o.ObjectID == objectId && o.AttributeName == attributeName).FirstOrDefault();

			if (cachedObj != null)
				return cachedObj;
			else
			{

				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + String.Format("/{0}/{1}/asset/{2}", objectType, objectId, attributeName);
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);

					DTO.Assets.AssetAttributeValue[] results = Deserialize<DTO.Assets.AssetAttributeValue[]>(resultString);
					if (results.Length == 1)
					{
						AttributeValueFacade av = new AttributeValueFacade();
						av.ObjectType = objectType;
						av.ObjectID = objectId;
						av.AttributeName = attributeName;
						av.AssetAttributeValueID = results[0].AssetAttributeValueID;
						av.Value = results[0].Value;
						if (results[0].ValueID.HasValue)
							av.ValueID = results[0].ValueID.Value;

						return av;
					}
					else
					{
						return null;
					}
				}
			}
		}

		public List<AttributeValueFacade> GetAssetValues(string objectType, string objectId, string attributeName)
		{
			var cachedObjects = _attCache.Where(o => o.ObjectType == objectType && o.ObjectID == objectId && o.AttributeName == attributeName).ToList();

			if (cachedObjects != null && cachedObjects.Count > 0)
				return cachedObjects;
			else
			{
				List<AttributeValueFacade> attributeValues = new List<AttributeValueFacade>();

				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + String.Format("/{0}/{1}/asset/{2}", objectType, objectId, attributeName);
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);

					DTO.Assets.AssetAttributeValue[] results = Deserialize<DTO.Assets.AssetAttributeValue[]>(resultString);
					foreach (var result in results)
					{
						AttributeValueFacade av = new AttributeValueFacade();
						av.ObjectType = objectType;
						av.ObjectID = objectId;
						av.AttributeName = attributeName;
						av.AssetAttributeValueID = result.AssetAttributeValueID;
						av.Value = result.Value;
						if (result.ValueID.HasValue)
							av.ValueID = result.ValueID.Value;

						attributeValues.Add(av);
					}
				}
				return attributeValues;
			}
		}

		public bool CreateAssetValue(string objectType, string objectId, string assetName, string assetValueId, string assetValue)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				string uri = _baseUrl + String.Format("/{0}/{1}/asset/{2}?AssetValueID={3}", objectType, objectId, assetName, assetValueId);
				string httpMethod = "POST";
				string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
				wc.Headers.Add("Authorization", authHeader);
				wc.Headers.Add("UserAgent", "convergence.net/8.6.15(CSE+1.8.3.7)");
				wc.Headers.Add("Content-Type", "application/xml");

				string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<String>(assetValue));

				DTO.ServiceResult<DTO.Assets.Asset> result = Deserialize<DTO.ServiceResult<DTO.Assets.Asset>>(resultString);

				if (result.IsSuccess())
					return true;
				else
					return false;
			}
		}

		public bool UpdateAssetValue(string objectType, string objectId, string assetAttributeValueId, string assetValueId, string assetValue)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				string uri = _baseUrl + String.Format("/{0}/{1}/asset/{2}?AssetValueID={3}", objectType, objectId, assetAttributeValueId, assetValueId);
				string httpMethod = "PUT";
				string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
				wc.Headers.Add("Authorization", authHeader);
				wc.Headers.Add("UserAgent", "convergence.net/8.6.15(CSE+1.8.3.7)");
				wc.Headers.Add("Content-Type", "application/xml");

				string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<String>(assetValue));

				DTO.ServiceResult<DTO.Assets.Asset> result = Deserialize<DTO.ServiceResult<DTO.Assets.Asset>>(resultString);

				if (result.IsSuccess())
					return true;
				else
					return false;
			}
		}

		/* NodeAsset API Calls

    public DTO.Assets.AssetAttributeValue GetNodeAssetValue(string objectType, string objectId, string attributeName)
    {
      using (System.Net.WebClient wc = new System.Net.WebClient())
      {
        string uri = _baseUrl + String.Format("/{0}/{1}/nodeasset/{2}", objectType, objectId, attributeName);
        string httpMethod = "GET";
        string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
        wc.Headers.Add("Authorization", authHeader);
        uri.Dump();
        string resultString = wc.DownloadString(uri);

        DTO.Assets.AssetAttributeValue[] results = Deserialize<DTO.Assets.AssetAttributeValue[]>(resultString);
        if (results.Length == 1)
        {
          return results[0];
        }
        else {
          return null;
        }
      }
    }

    public bool CreateNodeAssetValue(string objectType, string objectId, string assetName, string assetValueId, string assetValue)
    {
      using (System.Net.WebClient wc = new System.Net.WebClient())
      {
        string uri = _baseUrl + String.Format("/{0}/{1}/nodeasset/{2}?AssetValueID={3}", objectType, objectId, assetName, assetValueId);
        string httpMethod = "POST";
        string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
        wc.Headers.Add("Authorization", authHeader);
        wc.Headers.Add("UserAgent", "convergence.net/8.6.15(CSE+1.8.3.7)");
        wc.Headers.Add("Content-Type", "application/xml");

        string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<String>(assetValue));

        DTO.ServiceResult<DTO.Assets.Asset> result = Deserialize<DTO.ServiceResult<DTO.Assets.Asset>>(resultString);
        result.Dump("CreateNodeAssetValue");

        if (result.IsSuccess())
          return true;
        else
          return false;
      }
    }

    public bool UpdateNodeAssetValue(string objectType, string objectId, string assetAttributeValueId, string assetValueId, string assetValue)
    {
      using (System.Net.WebClient wc = new System.Net.WebClient())
      {
        string uri = _baseUrl + String.Format("/{0}/{1}/nodeasset/{2}?AssetValueID={3}", objectType, objectId, assetAttributeValueId, assetValueId);
        string httpMethod = "PUT";
        string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
        wc.Headers.Add("Authorization", authHeader);
        wc.Headers.Add("UserAgent", "convergence.net/8.6.15(CSE+1.8.3.7)");
        wc.Headers.Add("Content-Type", "application/xml");

        string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<String>(assetValue));

        DTO.ServiceResult<DTO.Assets.Asset> result = Deserialize<DTO.ServiceResult<DTO.Assets.Asset>>(resultString);

        if (result.IsSuccess())
          return true;
        else
          return false;
      }
    }
    */

		#endregion

		#region Completion Records

		public List<CompletionFacade> GetCompletionsByDate(Guid contextNodeUid, DateTime startDate, DateTime endDate)
		{
			List<CompletionFacade> objects = new List<CompletionFacade>();

			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + String.Format("/node/{0}/completions?from={1}&to={2}", contextNodeUid, startDate, endDate);
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);

					if (String.IsNullOrEmpty(resultString))
						return objects;
					else
					{
						DTO.Registry.CompletionRecord[] results = Deserialize<DTO.Registry.CompletionRecord[]>(resultString);

						foreach (DTO.Registry.CompletionRecord result in results)
						{
							CompletionFacade cf = new CompletionFacade(result);
							objects.Add(cf);
						}

					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
					Console.WriteLine(ex);
				}
				return objects;
			}


		}

		#endregion

		#region Catalog

		public CatalogFacade GetCatalog(string catalogUid)
		{
			using (System.Net.WebClient wc = new System.Net.WebClient())
			{
				try
				{
					string uri = _baseUrl + String.Format("/catalog/{0}", catalogUid);
					string httpMethod = "GET";
					string authHeader = GetAuthHeader(_oauth, uri, httpMethod, _username, _secret);
					wc.Headers.Add("Authorization", authHeader);

					string resultString = wc.DownloadString(uri);

					if (String.IsNullOrEmpty(resultString))
						return null;
					else
					{
						DTO.Catalog.Catalog result = Deserialize<DTO.Catalog.Catalog>(resultString);

						CatalogFacade catalog = new CatalogFacade();
						catalog.CatalogUID = result.CatalogUID;
						catalog.CatalogID = result.CatalogID;

						catalog.PriceListItems = new List<PriceListFacade>();

						foreach (var pl in result.PriceList)
						{
							if (pl.NodeAssignment != null)
							{
								PriceListFacade plf = new PriceListFacade();
								plf.PriceListUID = pl.PriceListUID;
								plf.Price = pl.Price;

								if (pl.NodeAssignment.Activity != null)
								{
									plf.ActivityID = pl.NodeAssignment.ActivityID;
								}
								else if (pl.NodeAssignment.Qualification != null)
								{
									plf.QualificationID = pl.NodeAssignment.QualificationID;
								}

								catalog.PriceListItems.Add(plf);
							}

						}

						return catalog;
					}
				}
				catch (WebException wex)
				{
					string convErrorMessage = GetConvErrorMessage(wex.Response);
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
				}
				catch (Exception ex)
				{
					log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
				}
				return null;
			}
		}

		public PriceListFacade CreatePriceListItem(int catalogId, Guid catalogUid, int? activityId, int? qualificationId, decimal price)
		{
			try
			{
				DTO.Catalog.PriceList newPriceListItem = new DTO.Catalog.PriceList()
				{
					CatalogID = catalogId,
					WholesalePrice = 0,
					MSRPrice = 0,
					Price = price,
					NodeAssignment = new DTO.Registry.NodeAssignment()
					{
						ActivityID = (activityId.HasValue) ? (activityId.Value) : ((int?)null),
						QualificationID = (qualificationId.HasValue) ? (qualificationId.Value) : ((int?)null),
						LaunchRules = new List<DTO.Registry.NodeAssignmentLaunchRule>()
					},
				};
				newPriceListItem.NodeAssignment.LaunchRules.Add(new DTO.Registry.NodeAssignmentLaunchRule()
				{
					NodeAssignmentLaunchRuleID = -1,
					DTOState = DTO.State.Created,
					NodeState = DTO.Directory.NodeStates.Active,
					LaunchRuleTemplateID = 1,
					ParentObjectName = "daystolaunch",
					Value = "30",
				});
				newPriceListItem.NodeAssignment.LaunchRules.Add(new DTO.Registry.NodeAssignmentLaunchRule()
				{
					NodeAssignmentLaunchRuleID = -2,
					DTOState = DTO.State.Created,
					NodeState = DTO.Directory.NodeStates.Active,
					LaunchRuleTemplateID = 2,
					ParentObjectName = "daystocomplete",
					Value = "2",
				});
				newPriceListItem.NodeAssignment.Schedules = (new DTO.Registry.NodeAssignmentSchedule[] {
																							new DTO.Registry.NodeAssignmentSchedule() {
																								DTOState = DTO.State.Created,
																								NodeState = DTO.Directory.NodeStates.Active,
																								StartDate = DateTime.Today
																							}
																						}).ToList();
				newPriceListItem.NodeAssignment.ScheduleCount = 1;


				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + String.Format("/catalog/{0}/pricelist", catalogUid);
					string httpMethod = "POST";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Catalog.PriceList>(newPriceListItem));

					if (String.IsNullOrEmpty(resultString))
					{
						throw new Exception("Error creating PriceListItem for '" + ((activityId.HasValue) ? (activityId.Value) : (qualificationId.Value)).ToString() + "'");
					}
					else
					{
						DTO.ServiceResult<DTO.Catalog.PriceList> result = Deserialize<DTO.ServiceResult<DTO.Catalog.PriceList>>(resultString);

						if (result.IsSuccess())
						{
							PriceListFacade plf = new PriceListFacade();
							plf.Price = result.Object.Price;
							plf.PriceListUID = result.Object.PriceListUID;

							if (result.Object.NodeAssignment.Activity != null)
							{
								plf.ActivityID = result.Object.NodeAssignment.ActivityID;
							}
							else if (result.Object.NodeAssignment.Qualification != null)
							{
								plf.QualificationID = result.Object.NodeAssignment.QualificationID;
							}

							return plf;
						}
						else
						{
							return null;
						}


					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}

		public PriceListFacade UpdatePriceListItem(int catalogId, Guid catalogUid, Guid priceListItemUid, int? activityId, int? qualificationId, decimal price)
		{
			try
			{
				DTO.Catalog.PriceList newPriceListItem = new DTO.Catalog.PriceList()
				{
					PriceListUID = priceListItemUid,
					CatalogID = catalogId,
					WholesalePrice = 0,
					MSRPrice = 0,
					Price = price,
					NodeAssignment = new DTO.Registry.NodeAssignment()
					{
						ActivityID = (activityId.HasValue) ? (activityId.Value) : ((int?)null),
						QualificationID = (qualificationId.HasValue) ? (qualificationId.Value) : ((int?)null),
					}
				};


				using (System.Net.WebClient wc = new System.Net.WebClient())
				{
					string uri = _baseUrl + String.Format("/catalog/{0}/pricelist", catalogUid);
					string httpMethod = "PUT";
					DoOathAndHeaders(wc, _oauth, uri, httpMethod, _username, _secret);

					string resultString = wc.UploadString(uri, httpMethod, "<?xml version=\"1.0\"?>\r\n" + Serialize<DTO.Catalog.PriceList>(newPriceListItem));

					if (String.IsNullOrEmpty(resultString))
					{
						throw new Exception("Error updating PriceListItem for '" + ((activityId.HasValue) ? (activityId.Value) : (qualificationId.Value)).ToString() + "'");
					}
					else
					{
						DTO.ServiceResult<DTO.Catalog.PriceList> result = Deserialize<DTO.ServiceResult<DTO.Catalog.PriceList>>(resultString);

						if (result.IsSuccess())
						{
							PriceListFacade plf = new PriceListFacade();
							plf.Price = result.Object.Price;
							plf.PriceListUID = result.Object.PriceListUID;

							if (result.Object.NodeAssignment.Activity != null)
							{
								plf.ActivityID = result.Object.NodeAssignment.ActivityID;
							}
							else if (result.Object.NodeAssignment.Qualification != null)
							{
								plf.QualificationID = result.Object.NodeAssignment.QualificationID;
							}

							return plf;
						}
						else
						{
							return null;
						}


					}
				}
			}
			catch (WebException wex)
			{
				string convErrorMessage = GetConvErrorMessage(wex.Response);
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name + convErrorMessage, wex);
			}
			catch (Exception ex)
			{
				log.Error(System.Reflection.MethodBase.GetCurrentMethod().Name, ex);
			}
			return null;
		}


		#endregion

		#region Helper Methods

		public string GetConvErrorMessage(WebResponse response)
		{
			string msg = GetResponseHeaderValue(response, "ConvergenceError");
			if (!String.IsNullOrEmpty(msg))
				return String.Format(" - {0}", msg);
			else
				return msg;
		}

		public string GetResponseHeaderValue(WebResponse response, string key)
		{
			string result = "";

			if (response != null && !String.IsNullOrEmpty(key) && response.Headers != null && response.Headers.AllKeys.Contains(key) && response.Headers[key] != null)
			{
				result = response.Headers[key].ToString();
			}

			return result;
		}

		public void PingContentUploader()
		{
			log.InfoFormat("Making GET Request to Content Upload url...");

			string contentUploaderUrl = this._baseUrl.Replace("/services4/publicservice.svc", "/Convergence.ContentHost/uploader/upload.aspx");
			log.InfoFormat("\tUrl: {0}", contentUploaderUrl);

			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(contentUploaderUrl);

			using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
			{
				if (response.StatusCode == HttpStatusCode.OK)
				{
					log.Info("\t\tSuccess!");
				}
				else
				{
					throw new Exception($"Unable to connect to the uploader at '{contentUploaderUrl}'");
				}
			}
		}

		public bool SetAllowUnsafeHeaderParsing20()
		{
			log.InfoFormat("SetAllowUnsafeHeaderParsing20 for TLS 1.2");

			ServicePointManager.SecurityProtocol = (SecurityProtocolType)768 | (SecurityProtocolType)3072;

			//Get the assembly that contains the internal class
			Assembly aNetAssembly = Assembly.GetAssembly(typeof(System.Net.Configuration.SettingsSection));
			if (aNetAssembly != null)
			{
				//Use the assembly in order to get the internal type for the internal class
				Type aSettingsType = aNetAssembly.GetType("System.Net.Configuration.SettingsSectionInternal");
				if (aSettingsType != null)
				{
					//Use the internal static property to get an instance of the internal settings class.
					//If the static instance isn't created allready the property will create it for us.
					object anInstance = aSettingsType.InvokeMember("Section",
					BindingFlags.Static | BindingFlags.GetProperty | BindingFlags.NonPublic, null, null, new object[] { });
					if (anInstance != null)
					{
						//Locate the private bool field that tells the framework is unsafe header parsing should be allowed or not
						FieldInfo aUseUnsafeHeaderParsing = aSettingsType.GetField("useUnsafeHeaderParsing", BindingFlags.NonPublic | BindingFlags.Instance);
						if (aUseUnsafeHeaderParsing != null)
						{
							aUseUnsafeHeaderParsing.SetValue(anInstance, true);
							log.Info("\tSuccess!");
							return true;
						}
					}
				}
			}
			log.Info("\tDone");
			return false;
		}

		public DTO.Directory.Node CreateEmptyNode()
		{
			DTO.Directory.Node emptyNode = new DTO.Directory.Node()
			{
				DTOState = DTO.State.Created,
				NodeState = DTO.Directory.NodeStates.Active,
				NodeDetail = new DTO.Directory.NodeDetail()
				{
					DTOState = DTO.State.Created,
					NodeState = DTO.Directory.NodeStates.Active
				}
			};

			return emptyNode;
		}

		private static string Serialize<T>(T obj)
		{
			string result = string.Empty;
			DataContractSerializer dcs = new DataContractSerializer(typeof(T));

			using (MemoryStream stream = new MemoryStream())
			{
				dcs.WriteObject(stream, obj);
				stream.Seek(0, SeekOrigin.Begin);
				byte[] bytes = new byte[stream.Length];
				stream.Read(bytes, 0, bytes.Length);
				//result = System.Text.UTF8Encoding.ASCII.GetString(bytes);
				result = System.Text.UTF8Encoding.UTF8.GetString(bytes);
			}
			return result;
		}

		private static T Deserialize<T>(string data)
		{
			T result;
			DataContractSerializer dcs = new DataContractSerializer(typeof(T));

			byte[] bytes = System.Text.UTF8Encoding.UTF8.GetBytes(data);
			using (MemoryStream stream = new MemoryStream(bytes))
			{
				result = (T)dcs.ReadObject(stream);
			}
			return result;
		}

		private static void DoOathAndHeaders(System.Net.WebClient wc, OAuthBase oauth, string uri, string httpMethod, string user, string secret)
		{
			string authHeader = GetAuthHeader(oauth, uri, httpMethod, user, secret);
			wc.Headers.Add("Authorization", authHeader);
			wc.Headers.Add("UserAgent", "convergence.net/8.6.15(CSE+1.8.3.7)");
			wc.Headers.Add("Content-Type", "application/xml");
		}

		private static string GetAuthHeader(OAuthBase oauth, string uri, string httpMethod, string user, string secret)
		{
			//authHeader = "Authorization: OAuth oauth_nonce="1861532", oauth_signature_method="HMAC-SHA1", oauth_timestamp="1438814674", oauth_consumer_key="sean", oauth_signature="2vSFwR3z8cCVcJq1t2B9udM6TdE=", oauth_version="1.0"
			string authHeaderFormat = "OAuth oauth_nonce='{0}', oauth_signature_method='HMAC-SHA1', oauth_timestamp='{1}', oauth_consumer_key='{2}', oauth_signature='{3}', oauth_version='1.0'".Replace("'", "\"");
			string nonce = oauth.GenerateNonce();
			string timestamp = oauth.GenerateTimeStamp();
			string normalizedUrl = string.Empty;
			string normalizedRequestParameters = string.Empty;

			string hash = oauth.GenerateSignature(
				new Uri(uri),
				user,
				secret,
				null, // totken (3-leg oauth)
				null, //token secret (3-leg oauth)
				httpMethod,
				timestamp,
				nonce,
				out normalizedUrl,
				out normalizedRequestParameters
			);

			string authHeader = string.Format(authHeaderFormat
				, UrlEncodeRfc3986(nonce), UrlEncodeRfc3986(timestamp)
				, UrlEncodeRfc3986(user), UrlEncodeRfc3986(hash));
			//authHeader.Dump();
			return authHeader;
		}

		private static string UrlEncodeRfc3986(string s)
		{
			return Regex.Replace(Uri.EscapeDataString(s), @"[\!*\'\(\)]", m => Uri.HexEscape(Convert.ToChar(m.Value[0].ToString())));
		}

		#endregion
	}

}

