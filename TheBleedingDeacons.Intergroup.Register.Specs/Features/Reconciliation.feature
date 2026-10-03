Feature: Finishing the meeting
  As the volunteer on the door
  I want everything the tablet recorded sent to Unity when the meeting ends
  So that the intergroup's records are right without anyone typing them in twice

  When the meeting starts the tablet takes a snapshot of what Unity told
  it. Finishing compares the tablet's records with that snapshot and sends
  Unity only what changed: new members first (so that what follows can use
  the ids Unity gives them), then changed members, then consent, then who
  attended. If anything was refused, both logs are kept so that the next
  attempt can try again; if nothing was, they are cleared.

  Background:
    Given the group "Monday Step"
    And "Ann B" is a GSR of "Monday Step"
    And the meeting has started

  Scenario: Nothing changed, nothing is sent
    When the meeting is finished
    Then Unity is sent nothing

  Scenario: A registered group is sent with its GSR
    Given "Monday Step" has been registered
    When the meeting is finished
    Then Unity is told "Monday Step" attended, represented by "Ann B"
    And the meeting finished with 0 errors and 0 warnings

  Scenario: A stand-in is sent by name
    Given "Monday Step" has been registered with "Sam" standing in
    When the meeting is finished
    Then Unity is told "Monday Step" attended with "Sam" standing in

  Scenario: An officer is sent with the position they hold
    Given "Ann B" holds the position "Treasurer"
    And the meeting has started
    And "Treasurer" has been registered
    When the meeting is finished
    Then Unity is told "Ann B" attended as "Treasurer"

  Scenario: A group that was registered before and is not now is sent as absent
    Given the group "Wednesday Steps", already registered when the meeting started
    And "Wil G" is a GSR of "Wednesday Steps"
    And the meeting has started
    And "Wednesday Steps" has been unregistered
    When the meeting is finished
    Then Unity is told "Wednesday Steps" is no longer attending

  Scenario: Unity already having the registration is not a failure
    Given "Monday Step" has been registered
    And Unity already has registrations of groups
    When the meeting is finished
    Then the meeting finished with 0 errors and 0 warnings
    And both logs are cleared

  Scenario: Nothing refused, the logs are cleared
    Given "Monday Step" has been registered
    When the meeting is finished
    Then both logs are cleared

  Scenario: A refused registration keeps the logs for next time
    Given "Monday Step" has been registered
    And Unity refuses registrations of groups
    When the meeting is finished
    Then the meeting finished with 1 error and 0 warnings
    And the logs are kept for next time

  Rule: Members added at the meeting are created first

    A member added on the tablet has a temporary negative id that Unity has
    never seen. They are created before anything else is sent, and what
    follows uses the id Unity gives them.

    Scenario: A new GSR is created, and their group registered under the id Unity gave them
      Given the group "Friday Newcomers"
      And the meeting has started
      And "Fay H" is added at the meeting as a GSR of "Friday Newcomers"
      And "Friday Newcomers" has been registered
      When the meeting is finished
      Then Unity is asked to create "Fay H"
      And Unity is told "Friday Newcomers" attended, represented by member 900

    Scenario: A new member Unity will not create holds back their group, and the logs are kept
      Given the group "Friday Newcomers"
      And the meeting has started
      And "Fay H" is added at the meeting as a GSR of "Friday Newcomers"
      And "Friday Newcomers" has been registered
      And Unity refuses new members
      When the meeting is finished
      Then Unity is not told about "Friday Newcomers"
      And the meeting finished with 1 error and 0 warnings
      And the logs are kept for next time

  Rule: Only what changed is sent

    Scenario: A changed phone number is sent on its own
      When "Ann B"'s mobile number is changed to "07400 654321"
      And the meeting is finished
      Then Unity is told only "Ann B"'s new mobile number

    Scenario: An acceptance is sent with its version and policy
      Given the cached privacy policy is version "2.1"
      And "Ann B" has accepted the privacy policy
      When the meeting is finished
      Then Unity is told "Ann B" accepted version "2.1" of policy 42

  Rule: What a finished meeting does not hold on to

    These pin behaviour found while writing the specification, recorded in
    specs/domain-model.md rather than changed here.

    Scenario: A consent record Unity refuses is a warning, and the logs are cleared anyway
      Given the cached privacy policy is version "2.1"
      And "Ann B" has accepted the privacy policy
      And Unity refuses consent records
      When the meeting is finished
      Then the meeting finished with 0 errors and 1 warning
      And both logs are cleared

    Scenario: With no intergroup meeting chosen, attendance is not sent and the logs are cleared anyway
      Given there is no active intergroup meeting
      And "Monday Step" has been registered
      When the meeting is finished
      Then Unity is not told about "Monday Step"
      And both logs are cleared

  Scenario: A tablet that never took a snapshot syncs instead, and sends nothing
    Given the tablet has no snapshot
    And "Monday Step" has been registered
    When the meeting is finished
    Then Unity is not told about "Monday Step"
