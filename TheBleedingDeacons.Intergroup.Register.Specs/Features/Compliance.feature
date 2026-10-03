Feature: Recording consent
  As the volunteer on the door
  I want a member's acceptance of the privacy policy recorded the moment they give it
  So that the intergroup can show who agreed to what, and when

  An acceptance records the version agreed to, the policy's id and its
  wording, taken from the policy the tablet last fetched from Scrutiny. It
  is written to the database, then to the compliance log, and then the
  member is sent their own copy — which is theirs to keep whatever happens
  to the tablet or the site.

  Background:
    Given the group "Monday Step"
    And "Ann B" is a GSR of "Monday Step" with the email "ann@example.org"
    And the cached privacy policy is version "2.1"

  Scenario: An acceptance records the version, the policy and its wording
    When "Ann B" accepts the privacy policy
    Then "Ann B" has accepted version "2.1"
    And the acceptance records the policy's id and wording

  Scenario: Withdrawing consent clears what was accepted
    Given "Ann B" has accepted the privacy policy
    When "Ann B" withdraws their consent
    Then "Ann B" has no acceptance on record

  Scenario: The member is sent a copy of what they accepted
    When "Ann B" accepts the privacy policy
    Then "ann@example.org" is sent a copy of version "2.1"
    And replies to it go to "privacy@aa-bristol.org"

  Scenario: Not when the tablet is set otherwise
    Given acceptance emails are switched off
    When "Ann B" accepts the privacy policy
    Then "Ann B" has accepted version "2.1"
    And nobody is emailed

  Scenario: A member with only a phone number is recorded and not emailed
    Given "Dee E" is a GSR of "Monday Step"
    When "Dee E" accepts the privacy policy
    Then "Dee E" has accepted version "2.1"
    And nobody is emailed

  Scenario: Withdrawing sends nothing
    Given "Ann B" has accepted the privacy policy
    And the email queue has been emptied
    When "Ann B" withdraws their consent
    Then nobody is emailed

  Scenario: The acceptance is in the compliance log too
    When "Ann B" accepts the privacy policy
    Then the compliance log says "Ann B" accepted version "2.1"

  Scenario: With the compliance log switched off, only the database holds it
    Given the compliance log is switched off
    When "Ann B" accepts the privacy policy
    Then "Ann B" has accepted version "2.1"
    But the compliance log is empty

  Scenario: A tablet that lost its database gets the acceptance back from the log
    Given "Ann B" has accepted the privacy policy
    And the tablet's database loses today's acceptances
    When the compliance log is replayed
    Then "Ann B" has accepted version "2.1"
