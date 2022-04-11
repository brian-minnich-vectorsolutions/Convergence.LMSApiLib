using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Net.Cache;
using DTO = Convergence.Training.Server.DTO;

namespace Convergence.LMSApiLib
{
	public enum PlayMode
	{
		standardAndFullScreen = 100000000,
		standardOnly = 100000001,
		fullScreenOnly = 100000002,
		fullScreenOnlyWhenMobile = 100000003
	}

	public class LaunchParameters
	{
		public PlayMode? PlayMode { get; set; }
		public Decimal? PassingScore { get; set; }

		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();

			if (PlayMode.HasValue)
				sb.Append("playMode=" + PlayMode.Value.ToString() + "&");
			if (PassingScore.HasValue)
				sb.Append("passingScore=" + PassingScore.Value.ToString("N0") + "&");

			string launchParams = sb.ToString();

			//trim trailing ampersand
			if (launchParams.Last() == Char.Parse("&"))
			{
				launchParams = launchParams.Substring(0, launchParams.Length - 1);
			}

			return launchParams;
		}


		public bool HasValues
		{
			get
			{
				if (this.PlayMode.HasValue)
					return true;

				if (this.PassingScore.HasValue)
					return true;

				return false;
			}

		}
	}
	public class CompletionFacade
	{
		public int CompletionID;
		public string UserInfo;
		public string ActivityInfo;
		public DateTime CompletionDate;
		public string Description;

		public CompletionFacade(DTO.Registry.CompletionRecord cr)
		{
			this.CompletionID = cr.CompletionID;
			this.Description = cr.Description;
			this.CompletionDate = cr.CompletionDate;
			this.UserInfo = String.Format("{0} {1} ({2})", cr.User.Firstname, cr.User.Lastname, cr.User.Username);
			if (cr.Activity != null)
				this.ActivityInfo = String.Format("{0}", cr.Activity.Name);
		}
	}
	public class AttributeValueFacade
	{
		public int AssetAttributeValueID;
		public string ObjectType;
		public string ObjectID;
		public string AttributeName;
		public string Value;
		public int ValueID;
	}
	public class UserFacade
	{
		public string Username, Firstname, Lastname, Email, ExternalID;
		public int UserID, NodeID;
		public Guid UserUID, NodeUID;
	}
	public class NodeFacade
	{
		public string Name, ExternalID;
		public int ParentID, NodeID, TypeID, SubTypeID, ObjectID;
		public Guid NodeUID;
	}

	public class ActivityFacade
	{
		public string Name { get; set; }

		public int RegistryID, ActivityID;
		public string ExternalID, ExternalVersion;
		public string ThumbnailUrl;
		public int FileID;
		public Guid FileVersionUID;
		public int ParentLookupID;
		public string Culture; //Languages
		public int Duration;
		public string Description;
		public Guid FileUID;
		public string Field4;
	}

	public class SunsetCourse
	{
		public string ExternalID { get; set; }
		public string Name { get; set; }
		public DateTime SunsetDate { get; set; }
		public string Notes { get; set; }
		public string Culture { get; set; }
		public string ReplacmentCourseExternalId { get; set; }
	}

	public class ThumbnailFacade
	{
		public int ActivityID;
		public string Image;
	}

	public class FileFacade
	{
		public string Name;
		public int RepositoryID, FileID;
		public Guid FileUID;
		public string CRMVersion;
		public Guid FileVersionUID;
		public string ExternalID, Culture, ExternalVersion;
	}

	public class QualFacade
	{
		public string Name;
		public int RegistryID, QualificationID;
		public List<int> RequirementIds;
		public int ParentID;
		public string ExternalID, SKU;

		public List<ReqFacade> Requirements;

		public QualFacade() { }

		public QualFacade(DTO.Registry.Qualification qual, int? contextNodeId)
		{
			this.RegistryID = qual.RegistryID;
			this.Name = qual.Name;
			this.QualificationID = qual.QualificationID;
			this.SKU = qual.SKU;
			this.ExternalID = qual.ExternalID;

			if (contextNodeId.HasValue)
				this.ParentID = contextNodeId.Value;


			this.RequirementIds = new List<int>();
			this.Requirements = new List<ReqFacade>();
			if (qual.QualificationRequirements != null)
			{
				foreach (DTO.Registry.QualificationRequirement requirement in qual.QualificationRequirements.OrderBy(o => o.DisplayOrder))
				{
					this.RequirementIds.Add(requirement.RequirementID);
					this.Requirements.Add(new ReqFacade(requirement.Requirement, qual.NodeID)); //TODO: is this the right parent id? what do we need it for?
				}
			}
		}
	}

	public class ReqFacade
	{
		public string Name;
		public int RegistryID, RequirementID;
		public List<int> ActivityIds;
		public int ParentID;
		public string ExternalID;
		public string RegistryName;

		public List<ActivityFacade> Activities;

		public ReqFacade() { }

		public ReqFacade(DTO.Registry.Requirement req, int? parentId)
		{
			this.Name = req.Name;
			this.RegistryID = req.RegistryID;
			this.RequirementID = req.RequirementID;
			if (parentId.HasValue) this.ParentID = parentId.Value;
			this.ExternalID = req.ExternalID;
			this.RegistryName = req.Registry != null ? req.Registry.Name : null;

			this.ActivityIds = new List<int>();
			this.Activities = new List<ActivityFacade>();
			if (req.RequirementActivities != null)
			{
				foreach (DTO.Registry.RequirementActivity activity in req.RequirementActivities.OrderBy(o => o.DisplayOrder))
				{
					this.ActivityIds.Add(activity.ActivityID);
					this.Activities.Add(new ActivityFacade()
					{
						Name = activity.Activity.Name,
						ActivityID = activity.ActivityID
					});
				}
			}
		}
	}

	public class CatalogFacade
	{
		public Guid CatalogUID;
		public int CatalogID;
		public List<PriceListFacade> PriceListItems;
	}

	public class PriceListFacade
	{
		public Guid PriceListUID;
		public decimal Price;
		public int? ActivityID;
		public int? QualificationID;
		//TODO: Need file or culture or something?? Maybe...
	}


	public class GroupFacade : NodeFacade
	{
		public int? DynamicGroupTargetNodeID;
		public string DynamicGroupLogic;
		public List<GroupMemberFacade> Members;

		public GroupFacade()
		{

		}
	}

	public class GroupMemberFacade
	{
		public int UserID;
		public int GroupID;
	}

	public class AssignmentFacade
	{
		public int NodeID;
		public QualFacade Qualification;
		public ActivityFacade Activity;
	}
}
