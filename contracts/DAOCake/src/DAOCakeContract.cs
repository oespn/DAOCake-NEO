using System;
using System.ComponentModel;
using System.Numerics;

using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

using DAOCake.Classes;

namespace DAOCake
{
    [DisplayName("amaDAO.DAOCakeContract")]
    [ManifestExtra("Author", "Nathan Challen")]
    [ManifestExtra("Email", "nathan@amaDAO.io")]
    [ManifestExtra("Description", "Community contributions measured and sliced fairly.")]
    public class DAOCakeContract : SmartContract
    {
        #region Const Identifiers
        const byte Prefix_Transactions = 0x00;
        const byte Prefix_Org_Transactions = 0x01;

        const byte Prefix_Votes = 0x05;
        const byte Prefix_Trans_Votes = 0x06;

        const byte Prefix_Orgs = 0xAA;
        const byte Prefix_Org_Members = 0xAB;
        const byte Prefix_Members = 0xAC;
        const byte Prefix_Member_Orgs = 0xAD;
        const byte Prefix_User_Member_Lookup = 0x90;
        

        const byte Prefix_ContractOwner = 0xFF;
        const byte STRING_LIST  = 0xEE;
        #endregion

        #region Delegates & Events
        public delegate void OnNewTransDelegate(ByteString transId, ByteString orgId, string proof, UInt160 token, BigInteger amount);

        public delegate void OnVoteDelegate(ByteString transId, UInt160 byUser, ByteString orgId, bool voteFor, UInt16 tally, UInt16 ofRequired);

        public delegate void OnVotingDelegate(ByteString transId, UInt160 user, ByteString orgId, DecisionStatus decision, BigInteger total);


        public delegate void OnNewOrgMemberDelegate(ByteString orgId, ByteString memberId, string creatorName);

        public delegate void OnUpdateOrganisationDelegate(ByteString orgId, UInt16 voteForRequired); //ROADMAP: UInt16  voteDecisionCount,

        public delegate void OnNewOrganisationDelegate(ByteString orgId, string creatorName,  UInt16  voteForRequired); //ROADMAP: UInt16  voteDecisionCount,


        public delegate void OnLookupMemberDelegate(ByteString memberId);


        [DisplayName("NewTransaction")]
        public static event OnNewTransDelegate OnNewTransaction = default!;

        [DisplayName("Vote")]
        public static event OnVoteDelegate OnVote = default!;

        [DisplayName("VotingResolution")]
        public static event OnVotingDelegate OnVotingResolution = default!;


        [DisplayName("NewOrganisation")]
        public static event OnNewOrganisationDelegate OnNewOrganisation = default!;

        [DisplayName("UpdateOrganisation")]
        public static event OnUpdateOrganisationDelegate OnUpdateOrganisation = default!;

        [DisplayName("NewOrgMemberDelegate")]
        public static event OnNewOrgMemberDelegate OnNewOrgMember = default!;


        public static event OnLookupMemberDelegate OnLookupMember = default!;


        [DisplayName("StringChanged")]
        public static event Action<UInt160, string> OnStringChanged;
        #endregion

        #region Storage & Transaction Maps
        private static StorageMap ContractStorage => new StorageMap(Storage.CurrentContext, "DAOCakeContract");
        private static StorageMap ContractMetadata => new StorageMap(Storage.CurrentContext, "Metadata");

        private static Transaction Tx => (Transaction) Runtime.ScriptContainer;

        #endregion

        #region Remove Number Testing
        [DisplayName("NumberChanged")]
        public static event Action<UInt160, BigInteger> OnNumberChanged;

        public static bool ChangeNumber(BigInteger positiveNumber)
        {
            if (positiveNumber < 0)
            {
                throw new Exception("Only positive numbers are allowed.");
            }

            ContractStorage.Put(Tx.Sender, positiveNumber);
            OnNumberChanged(Tx.Sender, positiveNumber);
            return true;
        }

        public static ByteString GetNumber()
        {
            return ContractStorage.Get(Tx.Sender);
        }
        #endregion


        #region Org & Member

        // Security: Anyone can create an organsation (DAO)
        // The organisation has no value until it has members
        // TODO: Make userId come from Tx.Sender 
        public static void CreateOrganisation(string name, UInt160 user, string creatorName, ByteString? orgId, ByteString? memberId)
        {
            var token = Runtime.CallingScriptHash;

            if (orgId != null)
            {
                if (orgId.Length != 16) throw new Exception("org ID must be 16 bytes long");
            }
            else
            {
                var orgHash = new List<object>();
                orgHash.Add(orgId);
                orgHash.Add(user);
                orgHash.Add(Runtime.GetRandom());
                orgId = CryptoLib.Sha256(StdLib.Serialize(orgHash));
            }
            StorageMap orgMap = new(Storage.CurrentContext, Prefix_Orgs);
            var serializedTransaction = orgMap.Get(orgId);
            if (serializedTransaction != null) throw new Exception("specified orgId already exists");

            var oRow = new Organisation(); 
            oRow.OrgId = orgId;
            oRow.Name = name;
            oRow.CreatorUser = user;

            orgMap.Put(orgId, StdLib.Serialize(oRow));

            // Create the first memeber
            StorageMap membersMap = new(Storage.CurrentContext, Prefix_Members);

            var mRow = new Member();
            mRow.Name = creatorName;
            mRow.User = user;
            //mRow.OrgId = orgId;
            mRow.Decision = DecisionStatus.Approved; // Creator is automatcially approved without need for Vote
            
            if (memberId != null)
            {
                if (memberId.Length != 16) throw new Exception("member ID must be 16 bytes long");
            }
            else
            {
                var memberHash = new List<object>();
                memberHash.Add(orgId);
                memberHash.Add(user);
                memberHash.Add(Runtime.GetRandom());
                memberId = CryptoLib.Sha256(StdLib.Serialize(memberHash));
            }

            membersMap.Put(memberId, StdLib.Serialize(mRow)); 

            StorageMap orgMembersMap = new(Storage.CurrentContext, Prefix_Org_Members);
            orgMembersMap.Put(orgId + memberId, 0);

            //Add to index for Org list & 1-1 user lookup
            StorageMap memberOfOrgMap = new(Storage.CurrentContext, Prefix_Member_Orgs);
            memberOfOrgMap.Put(memberId + orgId, 0); 
            StorageMap memberUserMap = new(Storage.CurrentContext, Prefix_User_Member_Lookup);
            memberUserMap.Put(user + memberId, 0);   

            OnNewOrgMember(orgId, memberId, creatorName);
            OnNewOrganisation(orgId, creatorName, 1); //voteDecisionCount, voteForRequired);
        }


        public static void UpdateMemberVoteRules(ByteString orgId, UInt16 voteForRequired) //ROADMAP: UInt16? voteDecisionCount, 
        {
            if (orgId is null) throw new ArgumentException(nameof(orgId));
           
           // Temporary security:  See Roadmap.
            ByteString owner = ContractMetadata.Get("Owner");
            if (!Tx.Sender.Equals(owner))
            {
                 //ROADMAP: this function will to be made internal
                 // _updateMemberVoteRules will be called when sufficient votes are met according to current Org rules

                throw new Exception("Only the contract owner can do this");
            }

            StorageMap orgMap = new(Storage.CurrentContext, Prefix_Orgs);
            var serializedOrg = orgMap.Get(orgId);
            if (serializedOrg == null) throw new Exception("specified orgId does not exists");

            var org = (Organisation)StdLib.Deserialize(serializedOrg);
            // if (voteDecisionCount == null)
            // {
            //     voteDecisionCount = (UInt16)voteDecisionCount;
            // }
            //ROADMAP: org.VoteDecisionCount = (UInt16)voteDecisionCount;
            org.VoteForRequired = voteForRequired;

            orgMap.Put(orgId, StdLib.Serialize(org));
            
            OnUpdateOrganisation(orgId, voteForRequired); //ROADMAP: (UInt16)voteDecisionCount,
        }        

        // Security: only accessible internally
        //** PRIVATE INTERNAL FUNCTION
        public static void UpdateMemberAsApproved(ByteString orgId, ByteString transMemberId)
        {
            var memberId = transMemberId;
            var member = (Member)GetMemberObj(transMemberId);


            StorageMap membersMap = new(Storage.CurrentContext, Prefix_Members);
            member.Decision = DecisionStatus.Approved; 
            membersMap.Put(memberId, StdLib.Serialize(member));

            StorageMap memberOfOrgMap = new(Storage.CurrentContext, Prefix_Member_Orgs);
            memberOfOrgMap.Put(memberId + orgId, 0); 

            // Update MemberCount
            StorageMap orgMap = new(Storage.CurrentContext, Prefix_Orgs);
            var serializedOrg = orgMap.Get(orgId);
            var org = (Organisation)StdLib.Deserialize(serializedOrg);
            org.MemberCount++;
            orgMap.Put(orgId, StdLib.Serialize(org));

            OnNewOrgMember(orgId, memberId, "Approved:"+ member.Name);
        }


        // Security: Members must first be Approved by Voting
        public static void AddMemberOfOrg(ByteString orgId, string myName, ByteString? memberId) 
        {
            StorageMap orgMap = new(Storage.CurrentContext, Prefix_Orgs);
            var serializedTransaction = orgMap.Get(orgId);
            if (serializedTransaction == null) throw new Exception("Cannot find the orgId!");
            var org = (Organisation)StdLib.Deserialize(serializedTransaction);

            //Sender is the Member nominating themselves as joining
            //Other members must vote on them joining before they are added
            var userId = Tx.Sender;

            // Create new  member (pending approval)
            StorageMap daoMembers = new(Storage.CurrentContext, Prefix_Members);
            StorageMap membersOrgMap = new(Storage.CurrentContext, Prefix_Org_Members);

            var member = new Member();
            member.Name = myName;
            member.User = userId;
            //member.OrgId = orgId;

            if (memberId != null)
            {
                if (memberId.Length != 16) throw new Exception("member ID must be 16 bytes long");
            }
            else
            {
                var memberHash = new List<object>();
                memberHash.Add(orgId);
                memberHash.Add(userId);
                memberHash.Add(Runtime.GetRandom());
                memberId = CryptoLib.Sha256(StdLib.Serialize(memberHash));
            }

            daoMembers.Put(memberId, StdLib.Serialize(member)); 
            membersOrgMap.Put(orgId + memberId, 0); //index
            StorageMap memberUserMap = new(Storage.CurrentContext, Prefix_User_Member_Lookup);
            memberUserMap.Put(userId + memberId, 0);  //index 

            OnNewOrgMember(orgId, memberId, "Proposed:"+ myName);

            // Put out to voting
            StorageMap transMap = new(Storage.CurrentContext, Prefix_Transactions);
            var trans = new ProposalTransaction(); 
            // NEO Syntax note: params must be mapped 1-1 as , declaration fails to record data
            trans.OrgId = orgId;
            trans.User = userId;
            //tRow.EvidenceCID = memberId;
            trans.RefNo = myName;
            trans.ProposalType = ProposalType.NewMember;

            var transId = memberId;
            
            transMap.Put(transId, StdLib.Serialize(trans));

            StorageMap orgTransMap = new(Storage.CurrentContext, Prefix_Org_Transactions);
            orgTransMap.Put(orgId + transId, 0); // index map

            OnNewTransaction(transId, orgId, "New Member", null, 0);


        }
        #endregion

        // Security: User must be a member of the organisation
        // *** after testing remove passing in User (must be signed?)
        public static void Vote(ByteString transId, bool voteFor) 
        {
            var userId = Tx.Sender;

            var token = Runtime.CallingScriptHash;

            StorageMap votesMap = new(Storage.CurrentContext, Prefix_Votes);
            StorageMap votesTranMap = new(Storage.CurrentContext, Prefix_Trans_Votes);

            StorageMap transMap = new(Storage.CurrentContext, Prefix_Transactions);

            var serializedTransaction = transMap.Get(transId);
            if (serializedTransaction == null) throw new Exception("cannot find the transaction.");
            var trans = (ProposalTransaction)StdLib.Deserialize(serializedTransaction);

            if (!IsMemberOfOrg(trans.OrgId, userId, MemberQueryType.Approved)) throw new Exception("You can only vote if you are an Approved member!"); 

            if (userId == trans.User) throw new Exception("You cannot vote on your own proposal!"); 

            // Check if already voted
            var voteHash = new List<object>();
            voteHash.Add(transId);
            voteHash.Add(userId);
            // voteHash.Add(Runtime.GetRandom());
            var voteId = CryptoLib.Sha256(StdLib.Serialize(voteHash));

            var serializedVote = votesMap.Get(voteId);
            if (serializedVote != null) throw new Exception("you cannot Vote twice!");
        
            var vote = new Vote(); 
            // NEO Syntax note: params must be mapped 1-1 as , declaration fails to record data
            vote.TransId = transId;
            vote.VoteFor = voteFor;
            vote.User = userId;
            vote.Token = token;

            votesMap.Put(voteId, StdLib.Serialize(vote));
            votesTranMap.Put(transId + voteId, 0);

            // Assess Vote Resolution
            StorageMap orgMap = new(Storage.CurrentContext, Prefix_Orgs);
            var serializedOrgTransaction = orgMap.Get(trans.OrgId);
            if (serializedOrgTransaction == null) throw new Exception("Cannot find the orgId. Root cause: Invalid Transaction!");
            var org = (Organisation)StdLib.Deserialize(serializedOrgTransaction);

            if (!voteFor)
            {
                OnVote(transId, userId, trans.OrgId, voteFor, 0, org.VoteForRequired); //org.VoteDecisionCount, 
            }
            else
            { 
                //voteFor
                if (trans.Decision == DecisionStatus.Undecided)
                {
                    //Tally votes 
                    var votes = 0;
                    int votesFor = 0;
                
                    var transVotes = (Iterator<ByteString>)votesTranMap.Find(transId, FindOptions.KeysOnly | FindOptions.RemovePrefix);
                    foreach (var vId in transVotes)
                    {
                        var vRow = (Vote)StdLib.Deserialize(votesMap.Get(vId));
                        votes++;
                        if (vRow.VoteFor) votesFor++;
                    }
                    OnVote(transId, userId, trans.OrgId, voteFor, (UInt16) votesFor, org.VoteForRequired);

                    StorageMap daoMap = new(Storage.CurrentContext, Prefix_Org_Transactions);
                    if (votesFor >= org.VoteForRequired) // && votes >= org.VoteDecisionCount)
                    {
                        // Resolution reached: internal updates to related data
                        trans.Decision = DecisionStatus.Approved;
                        transMap.Put(transId, StdLib.Serialize(trans));

                        switch (trans.ProposalType) 
                        {
                            case ProposalType.NewMember:
                                UpdateMemberAsApproved(trans.OrgId, transId);
                                break;
                            case ProposalType.OrgRules:
                                UpdateMemberVoteRules(trans.OrgId, (UInt16) trans.Total);
                                break;
                            default:
                                // Normal work approval: no further action
                                break;
                        }

                        OnVotingResolution(transId, trans.User, trans.OrgId, trans.Decision, trans.Total);
                    }
                }
            }
        }



        // Security: User must be a member of the organisation
        // *** after testing remove passing in User (must be signed?)
        public static void CreateTransaction(ByteString orgId, UInt160 userId, BigInteger amount, string evidenceCID, string refNo, ByteString? transId)
        {
            var token = Runtime.CallingScriptHash;

            if (!IsMemberOfOrg(orgId, userId, MemberQueryType.Approved)) throw new Exception("You can only commit work if you are an Approved member!");

            StorageMap orgMap = new(Storage.CurrentContext, Prefix_Orgs);
            var serializedOrg = orgMap.Get(orgId);
            if (serializedOrg == null) throw new Exception("specified orgId does not exists");

            if (transId != null)
            {
                if (transId.Length != 16) throw new Exception("trans ID must be 16 bytes long");
            }
            else
            {
                var transHash = new List<object>();
                transHash.Add(orgId);
                transHash.Add(userId);
                transHash.Add(Runtime.GetRandom());
                transId = CryptoLib.Sha256(StdLib.Serialize(transHash));
            }

            StorageMap transMap = new(Storage.CurrentContext, Prefix_Transactions);
            var serializedTransaction = transMap.Get(transId);
            if (serializedTransaction != null) throw new Exception("specified transId already exists");

            var tRow = new ProposalTransaction(); 
            // NEO Syntax note: params must be mapped 1-1 as , declaration fails to record data
            tRow.OrgId = orgId;
            tRow.User = userId;
            tRow.EvidenceCID = evidenceCID;
            tRow.RefNo = refNo;
            tRow.Total = amount;
            tRow.Token = token;
           
            transMap.Put(transId, StdLib.Serialize(tRow));

            StorageMap orgTransMap = new(Storage.CurrentContext, Prefix_Org_Transactions);
            orgTransMap.Put(orgId + transId, 0); // index map

            OnNewTransaction(transId, orgId, evidenceCID, token, amount);
        }


        #region Data Retrieval Transactions

        [Safe]
        public static Map<ByteString, object> Votes(ByteString transId)
        {
            if (transId is null) throw new ArgumentException(nameof(transId));

            StorageMap transVotesMap = new(Storage.CurrentContext, Prefix_Trans_Votes);
            StorageMap votesMap = new(Storage.CurrentContext, Prefix_Votes);

            Map<ByteString, object> map = new();
            var transVotes = (Iterator<ByteString>)transVotesMap.Find(transId, FindOptions.KeysOnly | FindOptions.RemovePrefix);
            foreach (var voteId in transVotes)
            {
                var vRow = (Vote)StdLib.Deserialize(votesMap.Get(voteId));
                map[voteId] = vRow;
            }
            return map;
        }


        [Safe]
        public static Map<ByteString, object> MembersOfOrg(ByteString orgId)
        {
            if (orgId is null) throw new ArgumentException(nameof(orgId));

            StorageMap orgMembersMap = new(Storage.CurrentContext, Prefix_Org_Members);
            StorageMap membersMap = new(Storage.CurrentContext, Prefix_Members);

            Map<ByteString, object> map = new();
            var daoMembers = (Iterator<ByteString>)orgMembersMap.Find(orgId, FindOptions.KeysOnly | FindOptions.RemovePrefix);
            foreach (var mId in daoMembers)
            {
                var mRow = (Member)StdLib.Deserialize(membersMap.Get(mId));
                map[mId] = mRow;
            }
            return map;
        }

        [Safe]
        public static Map<ByteString, object> OrgsOfMember(ByteString memberId)
        {
            if (memberId is null) throw new ArgumentException(nameof(memberId));

            StorageMap memberOrgsMap = new(Storage.CurrentContext, Prefix_Member_Orgs);
            StorageMap orgsMap = new(Storage.CurrentContext, Prefix_Orgs);

            Map<ByteString, object> map = new();
            var orgs = (Iterator<ByteString>)memberOrgsMap.Find(memberId, FindOptions.KeysOnly | FindOptions.RemovePrefix);
            foreach (var oId in orgs)
            {
                var mRow = (Organisation)StdLib.Deserialize(orgsMap.Get(oId));
                map[oId] = mRow;
            }
            return map;
        }


        public static bool IsMemberOfOrg(ByteString orgId, UInt160? user, MemberQueryType status)
        {
            ByteString memberId = IsMember(user, status);
            if (memberId == null) return false;

            var orgsMap =  OrgsOfMember(memberId);
            foreach (var orgByteString in orgsMap.Keys)
            {
                if (orgByteString == orgId) return true;
            }
            return false;
        }

        public static ByteString IsMember(UInt160? user, MemberQueryType status)
        {
            if (user is null) 
            {
                user = Tx.Sender;
            }
            StorageMap userMemberMap = new(Storage.CurrentContext, Prefix_User_Member_Lookup);
            StorageMap membersMap = new(Storage.CurrentContext, Prefix_Members);

            var members = (Iterator<ByteString>)userMemberMap.Find(user, FindOptions.KeysOnly | FindOptions.RemovePrefix);
            Member member = null;
            ByteString memberId = null;
            foreach (var mId in members)
            {
                member = (Member)StdLib.Deserialize(membersMap.Get(mId));
                memberId = mId;
                OnLookupMember(mId);

                continue; // only one
            }

            if (members == null) return (ByteString)"0";
           
            if (member == null) 
                return null;
            else
            {
                switch (status) 
                {
                    case MemberQueryType.All:
                        return memberId;
                    case MemberQueryType.Approved:
                        if (member.Decision == DecisionStatus.Approved) return memberId;    
                        break;
                }
                
            }
            return memberId;
        }


        public static object GetMemberObj(ByteString memberId)
        {
            StorageMap membersMap = new(Storage.CurrentContext, Prefix_Members);

            var serializedMember = membersMap.Get(memberId);
            if (serializedMember == null) throw new Exception("memberId does not exist");
            var mRow = (Member)StdLib.Deserialize(serializedMember);
            return mRow;
        }

        [Safe]
        public static Map<ByteString, object> OrgsOfMemberByUser(UInt160? user)
        {
            var memberId = IsMember(user, MemberQueryType.All);
            return OrgsOfMember(memberId);

        }

        //if (!Tx.Sender.Equals(owner))

        // Returns an object with human readible attributes
        public static object GetTransObj(ByteString orgId, ByteString transId)
        {
            StorageMap transMap = new(Storage.CurrentContext, Prefix_Transactions);

            var tRow = (ProposalTransaction)StdLib.Deserialize(transMap.Get(transId));
            OnNewTransaction(transId, tRow.OrgId, tRow.EvidenceCID, tRow.Token, tRow.Total);
        
            return tRow;
        }

        // Returned strings are in base64 and need to be decoded for display e.g.: https://www.base64decode.org/
        public static Map<string, object> GetTransNotify(ByteString orgId, ByteString transId)
        {
            StorageMap transMap = new(Storage.CurrentContext, Prefix_Transactions);

            Map<string, object> sMap = new();
            {
                var tRow = (ProposalTransaction)StdLib.Deserialize(transMap.Get(transId));
                {
                    sMap["orgId"] = tRow.OrgId;
                    sMap["user"] = tRow.User;
                    sMap["proof"] = tRow.EvidenceCID;
                    sMap["total"] = tRow.Total;
                    sMap["token"] = tRow.Token;
                }
                OnNewTransaction(transId, tRow.OrgId, tRow.EvidenceCID, tRow.Token, tRow.Total);

            }
            return sMap;
        }

        internal static Map<ByteString, object> OwingsQuery(QueryType queryType, ByteString orgId, UInt160 user)
        {
            StorageMap daoMap = new(Storage.CurrentContext, Prefix_Org_Transactions);
            StorageMap transMap = new(Storage.CurrentContext, Prefix_Transactions);

            Map<ByteString, object> map = new();
            var daoTransactions = (Iterator<ByteString>)daoMap.Find(orgId, FindOptions.KeysOnly | FindOptions.RemovePrefix);
            foreach (var transId in daoTransactions)
            {
                var tRow = (ProposalTransaction)StdLib.Deserialize(transMap.Get(transId));
                //if ((filter != TransactionFilter.None) && !tRow.User.Equals(userId)) continue; 
                if (queryType == QueryType.User && !tRow.User.Equals(user)) continue; 
 
                map[transId] = tRow;
            }
            return map;
        }


        [Safe]
        public static Map<ByteString, object> OwingsToUser(ByteString orgId, UInt160 user)
        {
            if (orgId is null) throw new ArgumentException(nameof(orgId));
            if (user is null || !user.IsValid) throw new ArgumentException(nameof(user));

            
            Map<ByteString, object> map = new();
            map = OwingsQuery(QueryType.User, orgId, user);
           
            return map;
        }

        [Safe]
        public static Map<ByteString, object> OwingsByOrg(ByteString orgId)
        {
            if (orgId is null) throw new ArgumentException(nameof(orgId));

            
            Map<ByteString, object> map = new();
            map = OwingsQuery(QueryType.None, orgId, null);
           
            return map;
        }


        [Safe]
        public static Map<ByteString, object> Owings(UInt160 orgId)
        {
            if (orgId is null || !orgId.IsValid) throw new ArgumentException(nameof(orgId));

            StorageMap orgTransMap = new(Storage.CurrentContext, Prefix_Org_Transactions);
            StorageMap transMap = new(Storage.CurrentContext, Prefix_Transactions);

            Map<ByteString, object> map = new();
            var daoTransactions = (Iterator<ByteString>)orgTransMap.Find(orgId, FindOptions.KeysOnly | FindOptions.RemovePrefix);
            foreach (var transId in daoTransactions)
            {
                var tRow = (ProposalTransaction)StdLib.Deserialize(transMap.Get(transId));
                map[transId] = tRow;
            }
            return map;
        }

        [Safe]
        public static Map<ByteString, Map<string, object>> OwingsAsStringMap(UInt160 orgId)
        {
            //if (seller is null || !seller.IsValid) throw new ArgumentException(nameof(seller));

            StorageMap daoMap = new(Storage.CurrentContext, Prefix_Org_Transactions);
            StorageMap transMap = new(Storage.CurrentContext, Prefix_Transactions);

            Map<ByteString, Map<string, object>> map = new();
            var iterator = (Iterator<ByteString>)daoMap.Find(orgId, FindOptions.KeysOnly | FindOptions.RemovePrefix);
            foreach (var s in iterator)
            {
                var tRow = (ProposalTransaction)StdLib.Deserialize(transMap.Get(s));
                Map<string, object> sMap = new();
                sMap["user"] = tRow.User;
                sMap["proof"] = tRow.EvidenceCID;
                sMap["refno"] = tRow.RefNo;
                sMap["total"] = tRow.Total;
                map[s] = sMap;
            }
            return map;
        }
        #endregion



        #region Contract Management
        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (!update)
            {
                ContractMetadata.Put("Owner", (ByteString) Tx.Sender);
            }
        }


        public static void UpdateContract(ByteString nefFile, string manifest)
        {
            ByteString owner = ContractMetadata.Get("Owner");
            if (!Tx.Sender.Equals(owner))
            {
                throw new Exception("Only the contract owner can do this");
            }
            ContractManagement.Update(nefFile, manifest, null);
        }
        #endregion
    }
}
