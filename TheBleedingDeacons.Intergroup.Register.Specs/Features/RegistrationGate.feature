Feature: Who may be registered
  As the volunteer on the door
  I want the tablet to refuse a registration it could not follow up
  So that everyone registered can be reached, and has agreed to be

  Two gates stand in front of Yes. The first is contact: the intergroup
  must be able to reach someone. The second is consent: everyone being
  registered must have accepted the current privacy policy, and anyone who
  has not is asked, by name, before anything is written.

  Rule: Someone must be reachable

    The rule accepts a phone or an email. In practice every member has an
    email — Unity requires one, and so does the tablet's member form — so
    what actually stops a registration here is a group with no GSR at all.

    Scenario: A group with no GSR cannot be registered
      Then the group cannot be registered

    Scenario Outline: A holder can be reached by phone or by email
      Given a holder named "<name>" with phone "<phone>" and email "<email>"
      Then the group <verdict> be registered

      Examples:
        | name  | phone        | email           | verdict |
        | Ann B | 07400 123456 |                 | can     |
        | Ann B |              | ann@example.org | can     |
        | Ann B | 07400 123456 | ann@example.org | can     |
        | Ann B |              |                 | cannot  |
        |       | 07400 123456 | ann@example.org | cannot  |

    Scenario: One reachable holder among several is enough
      Given a holder named "Ann B" with phone "" and email ""
      And a holder named "Cal D" with phone "07400 123456" and email ""
      Then the group can be registered

    Scenario: A stand-in must give a name
      Given a holder named "Ann B" with phone "07400 123456" and email ""
      And someone is standing in without giving a name
      Then the group cannot be registered

    Scenario: A named stand-in is fine
      Given a holder named "Ann B" with phone "07400 123456" and email ""
      And someone is standing in as "Sam"
      Then the group can be registered

  Rule: Everyone registered has agreed to the current policy

    Background:
      Given the group "Monday Step"
      And the cached privacy policy is version "2.1"

    Scenario: A GSR who never accepted is asked
      Given "Ann B" is a GSR of "Monday Step"
      And "Ann B" has never accepted the privacy policy
      When consent is sought from the GSRs of "Monday Step"
      Then "Ann B" was asked
      And consent was given
      And "Ann B" has accepted version "2.1"

    Scenario: A GSR who accepted an earlier version is asked again
      Given "Ann B" is a GSR of "Monday Step"
      And "Ann B" accepted version "2.0" of the privacy policy
      When consent is sought from the GSRs of "Monday Step"
      Then "Ann B" was asked

    Scenario: A different version counts, not only an older one
      Given "Ann B" is a GSR of "Monday Step"
      And "Ann B" accepted version "3.0" of the privacy policy
      When consent is sought from the GSRs of "Monday Step"
      Then "Ann B" was asked

    Scenario: A GSR who accepted this version is not asked
      Given "Ann B" is a GSR of "Monday Step"
      And "Ann B" accepted version "2.1" of the privacy policy
      When consent is sought from the GSRs of "Monday Step"
      Then nobody was asked
      And consent was given

    Scenario: One refusal stops the round, and the acceptances before it stand
      Given "Ann B" is a GSR of "Monday Step"
      And "Bob C" is a GSR of "Monday Step"
      And "Cal D" is a GSR of "Monday Step"
      And "Bob C" will decline
      When consent is sought from the GSRs of "Monday Step"
      Then consent was refused
      And "Ann B" has accepted version "2.1"
      But "Cal D" was not asked

    Scenario: With no policy on the tablet, nobody is asked
      Given "Ann B" is a GSR of "Monday Step"
      And there is no cached privacy policy
      When consent is sought from the GSRs of "Monday Step"
      Then nobody was asked
      And consent could not be sought because there is no policy

    Scenario: A policy with no wording is not put in front of anyone
      Given "Ann B" is a GSR of "Monday Step"
      And the cached privacy policy has no wording
      When consent is sought from the GSRs of "Monday Step"
      Then nobody was asked
      And consent could not be sought because the policy is empty

    Scenario: An officer who comes with the group is asked too, from whichever group they belong to
      Given "Ann B" is a GSR of "Monday Step"
      And "Ann B" accepted version "2.1" of the privacy policy
      And "Ann B" holds the position "Treasurer"
      And the group "Thursday Women"
      And "Eve F" is a member of "Thursday Women"
      And "Eve F" holds the position "Treasurer"
      When consent is sought from the officers "Monday Step" brings with it
      Then "Eve F" was asked
      But "Ann B" was not asked
