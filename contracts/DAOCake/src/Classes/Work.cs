using System;
using System.Numerics;

using Neo;
using Neo.SmartContract.Framework;

namespace DAOCake.Classes
{
    public enum DecisionStatus { Undecided = 0, Approved = 1, Rejected = 2 };

    public enum QueryType { None, User };
    public enum MemberQueryType { All = 0, Approved = 1 };

    public enum ProposalType { Pay, NewMember, OrgRules }

    public class ProposalTransaction 
    {
        public ByteString OrgId = null;  // Integeration with external FE DB
        public UInt160 User = UInt160.Zero;
        public string EvidenceCID = string.Empty;
        public string RefNo = string.Empty;
        public BigInteger Total = 0; //DOU (DAO IOUs)

        //public DateTimeOffset? Created;
        public UInt160 Token = UInt160.Zero;
        public DecisionStatus Decision = DecisionStatus.Undecided;

        public ProposalType ProposalType = ProposalType.Pay;
    
    };

    // simple: single vote (by Org) == approve then move to multivotes
    public class Vote 
    {
        public UInt160 User = UInt160.Zero;

        public ByteString TransId = null; 

        public bool VoteFor = false; 

        public UInt160 Token = UInt160.Zero;

        //public DateTimeOffset? Created; // Get from Block number
    }

    public class Organisation 
    {
        public string Name = string.Empty;
        public ByteString OrgId = null;  // Internal index and external DB linkage
        public UInt160 CreatorUser = UInt160.Zero;  // Initial Signor

        public UInt16 MemberCount = 1;

        //ROADMAP: public UInt16 VoteDecisionCount = 1; // eg: 5
        public UInt16 VoteForRequired = 1; // eg: 3

        // eg: Out of 5 votes, 3 must be For to be Approved
        // when 5 vote are in OR 3 met then award.            
    }

    // All the members who have voting priviledes
    public class Member  
    {
        public string Name = string.Empty;
        public UInt160 User = UInt160.Zero; // Signor

        public DecisionStatus Decision = DecisionStatus.Undecided;       
    }

}