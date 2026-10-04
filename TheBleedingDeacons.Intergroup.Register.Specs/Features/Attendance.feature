Feature: Registering attendance
  As the volunteer on the door at an intergroup meeting
  I want a group or an officer marked as attending when they arrive
  So that the meeting's record is right, and each person hears that it is

  The tablet holds a copy of the intergroup's records for the length of
  the meeting. Registering writes to that copy; nothing goes to Unity until
  the meeting is finished (see Reconciliation.feature).

  Background:
    Given the group "Monday Step"
    And "Ann B" is a GSR of "Monday Step" with the email "ann@example.org"

  Scenario: Registering a group marks it attending
    When "Monday Step" is registered
    Then "Monday Step" is attending

  Scenario: A stand-in is recorded by name
    When "Monday Step" is registered with "Sam" standing in
    Then "Monday Step" is attending with "Sam" standing in

  Scenario: Unregistering a group marks it absent
    Given "Monday Step" has been registered
    When "Monday Step" is unregistered
    Then "Monday Step" is not attending

  Rule: A group brings its officers with it

    A position held by one of the group's members is registered along with
    the group, when the tablet is set to do so. Unregistering the group does
    not take the officer away again: they may be here in their own right,
    or through another group.

    Background:
      Given "Ann B" holds the position "Treasurer"

    Scenario: The position a GSR holds is registered with the group
      When "Monday Step" is registered
      Then "Treasurer" is attending

    Scenario: A position held by a member who is not the GSR comes too
      Given "Cal D" is a member of "Monday Step" with the email "cal@example.org"
      And "Cal D" holds the position "Secretary"
      When "Monday Step" is registered
      Then "Secretary" is attending

    Scenario: Not when the tablet is set otherwise
      Given positions are not registered with their group
      When "Monday Step" is registered
      Then "Treasurer" is not attending

    Scenario: Unregistering the group leaves its officer attending
      Given "Monday Step" has been registered
      When "Monday Step" is unregistered
      Then "Monday Step" is not attending
      But "Treasurer" is attending

    Scenario: Unregistering the group does not bring back an officer who left
      Given "Monday Step" has been registered
      And "Treasurer" has been unregistered
      When "Monday Step" is unregistered
      Then "Treasurer" is not attending

    Scenario: An officer can be registered without their group
      When "Treasurer" is registered
      Then "Treasurer" is attending
      But "Monday Step" is not attending

  Rule: Everyone registered is emailed once

    The welcome email is queued, never sent while the volunteer waits: the
    mail server is the queue's business (see EmailQueue.feature). Every
    member has an email address — Unity requires one, and so does the
    tablet's member form — so the scenario without one below is a guard,
    not a case anyone meets: such a member would be registered and simply
    not emailed.

    Scenario: The GSR is welcomed
      When "Monday Step" is registered
      Then "ann@example.org" is sent a welcome for "Monday Step"
      And 1 email is waiting to be sent

    Scenario: A GSR who also holds a position that came with the group is emailed once
      Given "Ann B" holds the position "Treasurer"
      When "Monday Step" is registered
      Then "ann@example.org" is sent a welcome for "Monday Step"
      And 1 email is waiting to be sent

    Scenario: A member whose position came with the group is welcomed too
      Given "Cal D" is a member of "Monday Step" with the email "cal@example.org"
      And "Cal D" holds the position "Secretary"
      When "Monday Step" is registered
      Then "cal@example.org" is sent a welcome for "Monday Step"
      And 2 emails are waiting to be sent

    Scenario: A member who is neither GSR nor officer is not
      Given "Cal D" is a member of "Monday Step" with the email "cal@example.org"
      When "Monday Step" is registered
      Then 1 email is waiting to be sent

    Scenario: A GSR with no email on record is still registered, and not emailed
      Given the group "Tuesday Big Book"
      And "Dee E" is a GSR of "Tuesday Big Book"
      When "Tuesday Big Book" is registered
      Then "Tuesday Big Book" is attending
      And nobody is emailed

    Scenario: Registering an officer directly welcomes the holder
      Given "Ann B" holds the position "Treasurer"
      When "Treasurer" is registered
      Then "ann@example.org" is sent a welcome for "Treasurer"

    Scenario: With welcome emails switched off, nobody is emailed
      Given welcome emails are switched off
      When "Monday Step" is registered
      Then "Monday Step" is attending
      And nobody is emailed

    Scenario: Unregistering sends nothing
      Given "Monday Step" has been registered
      And the email queue has been emptied
      When "Monday Step" is unregistered
      Then nobody is emailed
